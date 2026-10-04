using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Staff screens for discount codes. Anyone with discounts.view can see the list and details (that's also what lets a
/// cashier use codes at the till); creating, generating, editing, disabling and the storefront banner switch need
/// discounts.manage. Both are ordinary permissions on the Roles &amp; Permissions screen.
/// </summary>
[Authorize(Policy = Permissions.DiscountsView)]
public class DiscountsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IDiscountService _discounts;
    private readonly UserManager<ApplicationUser> _userManager;

    public DiscountsController(
        ApplicationDbContext context,
        IDiscountService discounts,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _discounts = discounts;
        _userManager = userManager;
    }

    // GET: /Discounts?status=active|scheduled|expired|disabled|all&search=
    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? search)
    {
        var now = DateTime.UtcNow;
        var query = _context.Discounts.AsNoTracking().Include(d => d.Targets).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(d => d.Code.Contains(term) || d.Name.Contains(term));
        }

        var all = await query.OrderByDescending(d => d.DateCreated).ToListAsync();

        var items = all.Select(d => new DiscountListItemViewModel
        {
            Discount = d,
            Status = d.StatusLabel(now),
            AppliesTo = DescribeTargets(d)
        }).ToList();

        var counts = items.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());

        status = string.IsNullOrWhiteSpace(status) ? "all" : status.ToLowerInvariant();
        if (status != "all")
        {
            items = items.Where(i => status switch
            {
                "active" => i.Status == "Active",
                "scheduled" => i.Status == "Scheduled",
                "expired" => i.Status is "Expired" or "Used up",
                "disabled" => i.Status == "Disabled",
                _ => true
            }).ToList();
        }

        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.Counts = counts;
        ViewBag.CanManage = CanManage();
        return View(items);
    }

    // GET: /Discounts/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var discount = await _context.Discounts.AsNoTracking()
            .Include(d => d.Targets)
            .FirstOrDefaultAsync(d => d.DiscountId == id);
        if (discount is null) return NotFound();

        var redemptions = await _context.DiscountRedemptions.AsNoTracking()
            .Where(r => r.DiscountId == id)
            .OrderByDescending(r => r.DateRedeemed)
            .Take(50)
            .ToListAsync();

        var orderIds = redemptions.Select(r => r.OrderId).ToList();
        var orderNumbers = await _context.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId, o => o.OrderNumber);

        var total = await _context.DiscountRedemptions.Where(r => r.DiscountId == id)
            .SumAsync(r => (decimal?)r.AmountDiscounted) ?? 0m;

        var labels = await TargetLabelsAsync(discount);

        ViewBag.CanManage = CanManage();
        return View(new DiscountDetailsViewModel
        {
            Discount = discount,
            Status = discount.StatusLabel(DateTime.UtcNow),
            TargetLabels = labels,
            RecentRedemptions = redemptions,
            OrderNumbers = orderNumbers,
            TotalDiscounted = total
        });
    }

    // GET: /Discounts/NewCode - used by the Generate button on the form.
    [HttpGet]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> NewCode(string? prefix)
        => Json(new { code = await _discounts.GenerateUniqueCodeAsync(prefix) });

    // GET: /Discounts/Create
    [HttpGet]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> Create()
    {
        var model = new DiscountFormViewModel();
        await FillChoicesAsync(model);
        return View(model);
    }

    // POST: /Discounts/Create - one code (typed or generated), or a batch of single-use generated codes.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> Create(DiscountFormViewModel model)
    {
        var bulk = model.BulkCount > 1;
        var typedCode = _discounts.NormalizeCode(model.Code);

        if (!bulk && typedCode.Length > 0 && await _context.Discounts.AnyAsync(d => d.Code == typedCode))
            ModelState.AddModelError(nameof(model.Code), $"The code '{typedCode}' already exists. Pick another or leave it blank to generate one.");

        if (!ModelState.IsValid)
        {
            await FillChoicesAsync(model);
            return View(model);
        }

        var userId = _userManager.GetUserId(User);
        var created = new List<Discount>();

        if (bulk)
        {
            // Each generated code is single-use by design - that's the point of a batch (gift codes, one per customer).
            for (var i = 1; i <= model.BulkCount; i++)
            {
                var d = BuildDiscount(model, new Discount(), userId);
                d.Code = await UniqueCodeWithinBatchAsync(model.CodePrefix, created);
                d.IsGenerated = true;
                d.MaxRedemptions = 1;
                d.Name = $"{model.Name.Trim()} #{i}";
                created.Add(d);
            }
        }
        else
        {
            var d = BuildDiscount(model, new Discount(), userId);
            if (typedCode.Length > 0)
            {
                d.Code = typedCode;
            }
            else
            {
                d.Code = await _discounts.GenerateUniqueCodeAsync(model.CodePrefix);
                d.IsGenerated = true;
            }
            created.Add(d);
        }

        _context.Discounts.AddRange(created);
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "DiscountCreated",
            Details = created.Count == 1
                ? $"Created discount '{created[0].Code}' ({created[0].ValueLabel()})."
                : $"Generated {created.Count} single-use discount codes for '{model.Name.Trim()}' ({created[0].ValueLabel()})."
        });
        await _context.SaveChangesAsync();

        if (created.Count == 1)
        {
            this.ToastSuccess($"Discount {created[0].Code} created.");
            return RedirectToAction(nameof(Details), new { id = created[0].DiscountId });
        }

        this.ToastSuccess($"{created.Count} single-use codes generated.");
        return RedirectToAction(nameof(Index), new { search = model.Name.Trim() });
    }

    // GET: /Discounts/Edit/5
    [HttpGet]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var d = await _context.Discounts.AsNoTracking().Include(x => x.Targets).FirstOrDefaultAsync(x => x.DiscountId == id);
        if (d is null) return NotFound();

        var model = new DiscountFormViewModel
        {
            DiscountId = d.DiscountId,
            Code = d.Code,
            Name = d.Name,
            Description = d.Description,
            Type = d.Type,
            Value = d.Value,
            MaxDiscountAmount = d.MaxDiscountAmount,
            MinimumSpend = d.MinimumSpend,
            MaxRedemptions = d.MaxRedemptions,
            MaxUsesPerCustomer = d.MaxUsesPerCustomer,
            Channel = d.Channel,
            StartsOn = d.StartsAt.ToLocalTime().Date,
            ValidForDays = d.ValidForDays,
            ExpiresOn = d.ExpiresAt?.ToLocalTime().Date,
            IsActive = d.IsActive,
            AutoApply = d.AutoApply,
            ShowBanner = d.ShowBanner,
            BannerText = d.BannerText,
            RedemptionCount = d.RedemptionCount,
            ProductIds = d.Targets.Where(t => t.Type == DiscountTargetType.Product && t.ProductId.HasValue).Select(t => t.ProductId!.Value).ToList(),
            SupplierIds = d.Targets.Where(t => t.Type == DiscountTargetType.Supplier && t.SupplierId.HasValue).Select(t => t.SupplierId!.Value).ToList(),
            Categories = d.Targets.Where(t => t.Type == DiscountTargetType.Category && t.Value != null).Select(t => t.Value!).ToList(),
            Brands = d.Targets.Where(t => t.Type == DiscountTargetType.Brand && t.Value != null).Select(t => t.Value!).ToList()
        };

        await FillChoicesAsync(model);
        return View(model);
    }

    // POST: /Discounts/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> Edit(int id, DiscountFormViewModel model)
    {
        if (id != model.DiscountId) return BadRequest();

        var d = await _context.Discounts.Include(x => x.Targets).FirstOrDefaultAsync(x => x.DiscountId == id);
        if (d is null) return NotFound();

        model.RedemptionCount = d.RedemptionCount;

        // A code that's already been used keeps its name on receipts and in reports, so it can't be renamed.
        var newCode = d.RedemptionCount > 0 ? d.Code : _discounts.NormalizeCode(model.Code);
        if (newCode.Length == 0)
            ModelState.AddModelError(nameof(model.Code), "A code is required.");
        else if (await _context.Discounts.AnyAsync(x => x.Code == newCode && x.DiscountId != id))
            ModelState.AddModelError(nameof(model.Code), $"The code '{newCode}' is already used by another discount.");

        if (model.MaxRedemptions.HasValue && model.MaxRedemptions.Value < d.RedemptionCount)
            ModelState.AddModelError(nameof(model.MaxRedemptions), $"This code has already been used {d.RedemptionCount} time(s) - the limit can't be lower than that.");

        if (!ModelState.IsValid)
        {
            await FillChoicesAsync(model);
            return View(model);
        }

        BuildDiscount(model, d, d.CreatedByUserId);
        d.Code = newCode;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "DiscountUpdated",
            Details = $"Updated discount '{d.Code}' ({d.ValueLabel()})."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Discount {d.Code} saved.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /Discounts/Toggle/5 - master on/off switch.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> Toggle(int id, string? returnTo)
    {
        var d = await _context.Discounts.FirstOrDefaultAsync(x => x.DiscountId == id);
        if (d is null) return NotFound();

        d.IsActive = !d.IsActive;
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = d.IsActive ? "DiscountEnabled" : "DiscountDisabled",
            Details = $"{(d.IsActive ? "Enabled" : "Disabled")} discount '{d.Code}'."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"{d.Code} is now {(d.IsActive ? "enabled" : "disabled")}.");
        return returnTo == "details" ? RedirectToAction(nameof(Details), new { id }) : RedirectToAction(nameof(Index));
    }

    // POST: /Discounts/ToggleBanner/5 - the seasonal-promotion banner switch.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> ToggleBanner(int id, string? returnTo)
    {
        var d = await _context.Discounts.FirstOrDefaultAsync(x => x.DiscountId == id);
        if (d is null) return NotFound();

        if (!d.ShowBanner && d.Channel == DiscountChannel.InStore)
        {
            this.ToastError("An in-store-only discount can't have a storefront banner. Edit it and change the channel to Online or Both first.");
            return RedirectToAction(nameof(Index));
        }

        d.ShowBanner = !d.ShowBanner;
        if (d.ShowBanner && string.IsNullOrWhiteSpace(d.BannerText))
            d.BannerText = DefaultBannerText(d);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = d.ShowBanner ? "DiscountBannerOn" : "DiscountBannerOff",
            Details = $"Storefront banner for '{d.Code}' switched {(d.ShowBanner ? "on" : "off")}."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess(d.ShowBanner
            ? $"Banner for {d.Code} is on - it shows on the storefront while the discount is live."
            : $"Banner for {d.Code} is off.");
        return returnTo == "details" ? RedirectToAction(nameof(Details), new { id }) : RedirectToAction(nameof(Index));
    }

    // POST: /Discounts/Delete/5 - only a code that was never used; anything used is disabled instead so history stays intact.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DiscountsManage)]
    public async Task<IActionResult> Delete(int id)
    {
        var d = await _context.Discounts.FirstOrDefaultAsync(x => x.DiscountId == id);
        if (d is null) return NotFound();

        if (d.RedemptionCount > 0)
        {
            this.ToastError($"{d.Code} has been used {d.RedemptionCount} time(s) and can't be deleted - disable it instead so past orders keep their history.");
            return RedirectToAction(nameof(Details), new { id });
        }

        _context.Discounts.Remove(d);
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "DiscountDeleted",
            Details = $"Deleted discount '{d.Code}'."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Discount {d.Code} deleted.");
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ helpers

    private bool CanManage() => User.HasClaim(Permissions.ClaimType, Permissions.DiscountsManage);

    private static string DefaultBannerText(Discount d) =>
        $"{d.ValueLabel()} - use code {d.Code}";

    /// <summary>Copies the form onto a Discount (new or existing) - dates, limits and the linked-catalogue targets.</summary>
    private static Discount BuildDiscount(DiscountFormViewModel m, Discount d, string? createdByUserId)
    {
        d.Name = m.Name.Trim();
        d.Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim();
        d.Type = m.Type;
        d.Value = Math.Round(m.Value ?? 0m, 2, MidpointRounding.AwayFromZero);
        d.MaxDiscountAmount = m.MaxDiscountAmount;
        d.MinimumSpend = m.MinimumSpend;
        d.MaxRedemptions = m.MaxRedemptions;
        d.MaxUsesPerCustomer = m.MaxUsesPerCustomer;
        d.Channel = m.Channel;
        d.IsActive = m.IsActive;
        d.AutoApply = m.AutoApply;
        d.ShowBanner = m.ShowBanner;
        d.BannerText = string.IsNullOrWhiteSpace(m.BannerText) ? null : m.BannerText.Trim();
        // (A blank banner text is fine: the storefront falls back to "15% off - use code ABC" itself.)

        // Dates are entered as local calendar days. Starting "today" (or leaving it blank) means starting right now.
        var startLocal = m.StartsOn?.Date;
        var todayLocal = DateTime.Now.Date;
        d.StartsAt = startLocal is null || startLocal.Value <= todayLocal
            ? (d.DiscountId == 0 ? DateTime.UtcNow : MinStart(d.StartsAt, startLocal))
            : DateTime.SpecifyKind(startLocal.Value, DateTimeKind.Local).ToUniversalTime();

        d.ValidForDays = m.ValidForDays;
        if (m.ValidForDays.HasValue)
            d.ExpiresAt = d.StartsAt.AddDays(m.ValidForDays.Value);
        else if (m.ExpiresOn.HasValue)
            d.ExpiresAt = DateTime.SpecifyKind(m.ExpiresOn.Value.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Local).ToUniversalTime();
        else
            d.ExpiresAt = null;

        if (d.DiscountId == 0)
        {
            d.CreatedByUserId = createdByUserId;
            d.DateCreated = DateTime.UtcNow;
        }

        // Replace the linked targets wholesale - simpler and safer than diffing.
        d.Targets.Clear();
        foreach (var pid in m.ProductIds.Distinct())
            d.Targets.Add(new DiscountTarget { Type = DiscountTargetType.Product, ProductId = pid });
        foreach (var sid in m.SupplierIds.Distinct())
            d.Targets.Add(new DiscountTarget { Type = DiscountTargetType.Supplier, SupplierId = sid });
        foreach (var cat in m.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
            d.Targets.Add(new DiscountTarget { Type = DiscountTargetType.Category, Value = cat.Trim() });
        foreach (var brand in m.Brands.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase))
            d.Targets.Add(new DiscountTarget { Type = DiscountTargetType.Brand, Value = brand.Trim() });

        return d;
    }

    /// <summary>On edit, keep the original start moment when the start day hasn't moved into the future.</summary>
    private static DateTime MinStart(DateTime existingUtc, DateTime? requestedLocalDay)
    {
        if (requestedLocalDay is null) return existingUtc;
        // The form shows the start as a day; if that's still the same local day, don't shift the stored moment.
        return existingUtc.ToLocalTime().Date == requestedLocalDay.Value.Date
            ? existingUtc
            : DateTime.SpecifyKind(requestedLocalDay.Value, DateTimeKind.Local).ToUniversalTime();
    }

    private async Task<string> UniqueCodeWithinBatchAsync(string? prefix, List<Discount> batchSoFar)
    {
        for (var i = 0; i < 10; i++)
        {
            var code = await _discounts.GenerateUniqueCodeAsync(prefix);
            if (!batchSoFar.Any(b => b.Code == code)) return code;
        }
        return $"{(string.IsNullOrWhiteSpace(prefix) ? "FIX" : prefix.ToUpperInvariant())}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
    }

    private async Task FillChoicesAsync(DiscountFormViewModel model)
    {
        model.ProductChoices = await _context.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ProductChoice { ProductId = p.ProductId, Name = p.Name, Sku = p.SKU })
            .ToListAsync();

        model.SupplierChoices = await _context.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SupplierChoice { SupplierId = s.SupplierId, Name = s.Name })
            .ToListAsync();

        var dbCategories = await _context.Products.AsNoTracking()
            .Select(p => p.Category).Distinct().ToListAsync();
        model.CategoryChoices = ProductViewModel.Categories
            .Concat(dbCategories)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        model.BrandChoices = await _context.Products.AsNoTracking()
            .Where(p => p.Brand != null && p.Brand != "")
            .Select(p => p.Brand!)
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync();
    }

    private static string DescribeTargets(Discount d)
    {
        if (d.Targets.Count == 0) return "Whole basket";
        var parts = new List<string>();
        var products = d.Targets.Count(t => t.Type == DiscountTargetType.Product);
        var suppliers = d.Targets.Count(t => t.Type == DiscountTargetType.Supplier);
        var categories = d.Targets.Where(t => t.Type == DiscountTargetType.Category).Select(t => t.Value).ToList();
        var brands = d.Targets.Where(t => t.Type == DiscountTargetType.Brand).Select(t => t.Value).ToList();
        if (products > 0) parts.Add($"{products} product{(products == 1 ? "" : "s")}");
        if (categories.Count > 0) parts.Add(string.Join(", ", categories));
        if (brands.Count > 0) parts.Add(string.Join(", ", brands));
        if (suppliers > 0) parts.Add($"{suppliers} supplier{(suppliers == 1 ? "" : "s")}");
        return string.Join(" · ", parts);
    }

    private async Task<List<string>> TargetLabelsAsync(Discount d)
    {
        var labels = new List<string>();

        var productIds = d.Targets.Where(t => t.Type == DiscountTargetType.Product && t.ProductId.HasValue).Select(t => t.ProductId!.Value).ToList();
        if (productIds.Count > 0)
        {
            var names = await _context.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => p.Name)
                .ToListAsync();
            labels.AddRange(names.Select(n => $"Product: {n}"));
        }

        var supplierIds = d.Targets.Where(t => t.Type == DiscountTargetType.Supplier && t.SupplierId.HasValue).Select(t => t.SupplierId!.Value).ToList();
        if (supplierIds.Count > 0)
        {
            var names = await _context.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.SupplierId))
                .Select(s => s.Name)
                .ToListAsync();
            labels.AddRange(names.Select(n => $"Supplier: {n}"));
        }

        labels.AddRange(d.Targets.Where(t => t.Type == DiscountTargetType.Category).Select(t => $"Category: {t.Value}"));
        labels.AddRange(d.Targets.Where(t => t.Type == DiscountTargetType.Brand).Select(t => $"Brand: {t.Value}"));
        return labels;
    }
}
