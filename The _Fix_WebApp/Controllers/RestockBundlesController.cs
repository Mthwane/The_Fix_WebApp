using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Saved restock templates ("Winter Outerwear Refill", "Monthly Basics Top-Up"). A manager builds
/// one once, then generates a Purchase Order from it in a click - optionally overriding
/// quantities for that run without changing the saved template.
///
/// Generating always produces a normal PurchaseOrder in Draft, so it still goes through the same
/// approval gate as a manually-raised one. A bundle is never itself an order.
/// </summary>
[Authorize(Policy = Permissions.PurchaseOrdersManage)]
public class RestockBundlesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public RestockBundlesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /RestockBundles?season=
    [HttpGet]
    public async Task<IActionResult> Index(RestockSeason? season)
    {
        var query = _context.RestockBundles
            .AsNoTracking()
            .Include(b => b.Supplier)
            .Include(b => b.Department)
            .Include(b => b.Items)
            .AsQueryable();

        if (season.HasValue) query = query.Where(b => b.Season == season.Value);

        ViewBag.SelectedSeason = season;
        return View(await query.OrderBy(b => b.Name).ToListAsync());
    }

    // GET: /RestockBundles/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var bundle = await _context.RestockBundles
            .AsNoTracking()
            .Include(b => b.Supplier)
            .Include(b => b.Department)
            .Include(b => b.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
            .Include(b => b.GeneratedPurchaseOrders)
            .FirstOrDefaultAsync(b => b.RestockBundleId == id);

        if (bundle is null) return NotFound();

        ViewBag.Suppliers = await _context.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        return View(bundle);
    }

    // GET: /RestockBundles/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        return View(new RestockBundle());
    }

    // POST: /RestockBundles/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RestockBundle model, List<int> variantIds, List<int> defaultQuantities, List<decimal?> defaultUnitCosts)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync();
            return View(model);
        }

        var bundle = new RestockBundle
        {
            Name = model.Name,
            Description = model.Description,
            Season = model.Season,
            SupplierId = model.SupplierId,
            DepartmentId = model.DepartmentId,
            Brand = model.Brand,
            IsActive = true,
            CreatedByUserId = _userManager.GetUserId(User)!
        };

        AddItems(bundle, variantIds, defaultQuantities, defaultUnitCosts);

        if (bundle.Items.Count == 0)
        {
            this.ToastError("Add at least one product line to the bundle.");
            await PopulateLookupsAsync();
            return View(model);
        }

        _context.RestockBundles.Add(bundle);
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Bundle '{bundle.Name}' saved with {bundle.Items.Count} line(s).");
        return RedirectToAction(nameof(Details), new { id = bundle.RestockBundleId });
    }

    // GET: /RestockBundles/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var bundle = await _context.RestockBundles
            .Include(b => b.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(b => b.RestockBundleId == id);

        if (bundle is null) return NotFound();

        await PopulateLookupsAsync();
        return View(bundle);
    }

    // POST: /RestockBundles/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RestockBundle model, List<int> variantIds, List<int> defaultQuantities, List<decimal?> defaultUnitCosts)
    {
        if (id != model.RestockBundleId) return BadRequest();

        var bundle = await _context.RestockBundles
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.RestockBundleId == id);

        if (bundle is null) return NotFound();

        bundle.Name = model.Name;
        bundle.Description = model.Description;
        bundle.Season = model.Season;
        bundle.SupplierId = model.SupplierId;
        bundle.DepartmentId = model.DepartmentId;
        bundle.Brand = model.Brand;
        bundle.IsActive = model.IsActive;
        bundle.DateUpdated = DateTime.UtcNow;

        // Lines are replaced wholesale rather than diffed: a template has no history worth
        // preserving, and purchase orders already generated from it keep their own copies.
        _context.RestockBundleItems.RemoveRange(bundle.Items);
        bundle.Items.Clear();
        AddItems(bundle, variantIds, defaultQuantities, defaultUnitCosts);

        await _context.SaveChangesAsync();

        this.ToastSuccess($"Bundle '{bundle.Name}' updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /RestockBundles/Generate/5 - turns the template into a Draft purchase order.
    // supplierId lets the raiser pick when the bundle has no default; quantity overrides apply
    // to this run only and never write back to the template.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int id, int? supplierId, List<int> itemIds, List<int> quantities)
    {
        var bundle = await _context.RestockBundles
            .Include(b => b.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(b => b.RestockBundleId == id);

        if (bundle is null) return NotFound();

        var resolvedSupplierId = supplierId ?? bundle.SupplierId;
        if (resolvedSupplierId is null)
        {
            this.ToastError("Choose a supplier - this bundle doesn't have a default one.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // Per-run quantity overrides, falling back to the template's defaults.
        var overrides = new Dictionary<int, int>();
        for (var i = 0; i < itemIds.Count && i < quantities.Count; i++)
            overrides[itemIds[i]] = quantities[i];

        var order = new PurchaseOrder
        {
            PONumber = await GeneratePoNumberAsync(),
            SupplierId = resolvedSupplierId.Value,
            CreatedByUserId = _userManager.GetUserId(User)!,
            Status = PurchaseOrderStatus.Draft,
            RestockBundleId = bundle.RestockBundleId,
            DateExpected = DateTime.UtcNow.AddDays(7),
            Notes = $"Generated from restock bundle '{bundle.Name}'."
        };

        foreach (var item in bundle.Items)
        {
            var qty = overrides.TryGetValue(item.RestockBundleItemId, out var o) ? o : item.DefaultQuantity;
            if (qty <= 0) continue; // zeroed out for this run

            order.Items.Add(new PurchaseOrderItem
            {
                ProductVariantId = item.ProductVariantId,
                QuantityOrdered = qty,
                // Pinned cost if the template has one, otherwise today's actual cost price -
                // avoids quoting a figure that went stale months ago.
                UnitCost = item.DefaultUnitCost ?? item.ProductVariant.Product?.CostPrice ?? 0
            });
        }

        if (order.Items.Count == 0)
        {
            this.ToastError("Every line was zeroed out - nothing to order.");
            return RedirectToAction(nameof(Details), new { id });
        }

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();

        this.ToastSuccess($"{order.PONumber} drafted from '{bundle.Name}' - review and submit it for approval.");
        return RedirectToAction("Details", "PurchaseOrders", new { id = order.PurchaseOrderId });
    }

    // POST: /RestockBundles/ToggleActive/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var bundle = await _context.RestockBundles.FindAsync(id);
        if (bundle is null) return NotFound();

        bundle.IsActive = !bundle.IsActive;
        bundle.DateUpdated = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        this.ToastSuccess(bundle.IsActive ? $"'{bundle.Name}' reactivated." : $"'{bundle.Name}' archived.");
        return RedirectToAction(nameof(Index));
    }

    // ===================== helpers =====================

    private static void AddItems(RestockBundle bundle, List<int> variantIds, List<int> quantities, List<decimal?> unitCosts)
    {
        if (variantIds is null) return;

        var seen = new HashSet<int>();
        for (var i = 0; i < variantIds.Count; i++)
        {
            var variantId = variantIds[i];
            // The unique index on (bundle, variant) would reject dupes at the DB level - catch
            // them here so the user gets a clean form instead of a constraint violation.
            if (!seen.Add(variantId)) continue;

            var qty = i < quantities.Count ? quantities[i] : 0;
            if (qty <= 0) continue;

            bundle.Items.Add(new RestockBundleItem
            {
                ProductVariantId = variantId,
                DefaultQuantity = qty,
                DefaultUnitCost = i < unitCosts.Count ? unitCosts[i] : null,
                DisplayOrder = i
            });
        }
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Suppliers = await _context.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        ViewBag.Departments = await _context.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.DisplayOrder).ToListAsync();
        ViewBag.Variants = await _context.ProductVariants.AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product.IsActive)
            .OrderBy(v => v.Product.Name).ThenBy(v => v.Size)
            .ToListAsync();
    }

    private async Task<string> GeneratePoNumberAsync()
    {
        var prefix = $"PO-{DateTime.UtcNow:yyyyMM}-";
        var existing = await _context.PurchaseOrders
            .Where(p => p.PONumber.StartsWith(prefix))
            .Select(p => p.PONumber)
            .ToListAsync();

        var set = existing.ToHashSet();
        for (var i = existing.Count + 1; ; i++)
        {
            var candidate = $"{prefix}{i:D4}";
            if (!set.Contains(candidate)) return candidate;
        }
    }
}
