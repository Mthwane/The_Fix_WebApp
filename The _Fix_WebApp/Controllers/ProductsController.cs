using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

[Authorize(Policy = Permissions.ProductsManage)]
public class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProductsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /Products?SearchTerm=&Category=&Size=&Color=&InStockOnly=
    // Master catalogue with search/filter (US-02).
    [HttpGet]
    public async Task<IActionResult> Index(ProductFilterViewModel filter)
    {
        var query = _context.Products.AsNoTracking().Where(p => p.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            query = query.Where(p =>
                p.Name.Contains(filter.SearchTerm) ||
                p.SKU.Contains(filter.SearchTerm));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
            query = query.Where(p => p.Category == filter.Category);

        if (!string.IsNullOrWhiteSpace(filter.Size))
            query = query.Where(p => p.Size == filter.Size);

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(p => p.Color == filter.Color);

        if (filter.InStockOnly == true)
            query = query.Where(p => p.StockQuantity > 0);

        var products = await query.OrderBy(p => p.Name).ToListAsync();

        ViewBag.Filter = filter;
        return View(products);
    }
    // GET: /Products/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        return View(new ProductViewModel());
    }

    // POST: /Products/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductViewModel model)
    {
        // SKU is never taken from the posted form - it's always server-generated,
        // so clear whatever ModelState has for it and re-validate without it.
        ModelState.Remove(nameof(ProductViewModel.SKU));

        // Category/Colour are a closed list on a real <select>, but nothing stops a
        // crafted POST from sending a value outside that list - reject it here rather
        // than trusting the browser to have enforced it.
        if (!ProductViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Please choose a category from the list.");
        if (!string.IsNullOrEmpty(model.Color) && !ProductViewModel.Colors.Contains(model.Color))
            ModelState.AddModelError(nameof(model.Color), "Please choose a colour from the list.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var product = new Product
        {
            Name = model.Name,
            Description = model.Description,
            SKU = await GenerateUniqueSkuAsync(model.Category),
            Category = model.Category,
            Size = model.Size,
            Color = model.Color,
            Brand = model.Brand,
            CostPrice = model.CostPrice,
            SellingPrice = model.SellingPrice,
            ImageUrl = model.ImageUrl,
            StockQuantity = model.StockQuantity,
            LowStockThreshold = model.LowStockThreshold,
            IsActive = true
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        await LogAuditAsync("ProductCreated", $"Added product '{product.Name}' (SKU {product.SKU}).");
        this.ToastSuccess($"'{product.Name}' was added to the catalogue.");

        return RedirectToAction(nameof(Index));
    }

    // GET: /Products/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null) return NotFound();

        await PopulateDropdownsAsync();

        var model = new ProductViewModel
        {
            ProductId = product.ProductId,
            Name = product.Name,
            Description = product.Description,
            SKU = product.SKU,
            Category = product.Category,
            Size = product.Size,
            Color = product.Color,
            Brand = product.Brand,
            CostPrice = product.CostPrice,
            SellingPrice = product.SellingPrice,
            ImageUrl = product.ImageUrl,
            StockQuantity = product.StockQuantity,
            LowStockThreshold = product.LowStockThreshold,
            IsActive = product.IsActive
        };

        return View(model);
    }

    // POST: /Products/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductViewModel model)
    {
        if (id != model.ProductId) return BadRequest();

        // SKU is read-only on Edit - never overwrite it from the posted form.
        ModelState.Remove(nameof(ProductViewModel.SKU));

        if (!ProductViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Please choose a category from the list.");
        if (!string.IsNullOrEmpty(model.Color) && !ProductViewModel.Colors.Contains(model.Color))
            ModelState.AddModelError(nameof(model.Color), "Please choose a colour from the list.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var product = await _context.Products.FindAsync(id);
        if (product is null) return NotFound();

        product.Name = model.Name;
        product.Description = model.Description;
        // product.SKU intentionally left unchanged - it's fixed at creation time.
        product.Category = model.Category;
        product.Size = model.Size;
        product.Color = model.Color;
        product.Brand = model.Brand;
        product.CostPrice = model.CostPrice;
        product.SellingPrice = model.SellingPrice;
        product.ImageUrl = model.ImageUrl;
        product.LowStockThreshold = model.LowStockThreshold;
        product.DateUpdated = DateTime.UtcNow;
        // NOTE: StockQuantity is intentionally NOT edited here directly - it should
        // only change via InventoryService (sales, returns, PO receipts, adjustments).

        await _context.SaveChangesAsync();

        await LogAuditAsync("ProductUpdated", $"Updated product '{product.Name}' (SKU {product.SKU}).");
        this.ToastSuccess($"'{product.Name}' was updated.");

        return RedirectToAction(nameof(Index));
    }

    // POST: /Products/Deactivate/5 - soft delete, never a hard DELETE.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null) return NotFound();

        product.IsActive = false;
        product.DateUpdated = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await LogAuditAsync("ProductDeactivated", $"Deactivated product '{product.Name}' (SKU {product.SKU}).");
        this.ToastSuccess($"'{product.Name}' was deactivated.");

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Builds the four attribute dropdowns. Category and Colour are a closed list - always
    /// exactly ProductViewModel.Categories / .Colors, picked from a real &lt;select&gt;, so no
    /// junk value typed on one product can ever leak into another product's dropdown. Size
    /// and Brand stay "self-sustaining": the seed list unioned with whatever's already used
    /// on existing products, so typing a new one there still works itself into future
    /// suggestions without a separate admin screen.
    /// </summary>
    private async Task PopulateDropdownsAsync()
    {
        ViewBag.Categories = ProductViewModel.Categories.ToList();
        ViewBag.Colors = ProductViewModel.Colors.ToList();

        var dbSizes = await _context.Products.AsNoTracking()
            .Where(p => p.Size != null && p.Size != "").Select(p => p.Size!).Distinct().ToListAsync();
        var dbBrands = await _context.Products.AsNoTracking()
            .Where(p => p.Brand != null && p.Brand != "").Select(p => p.Brand!).Distinct().ToListAsync();

        ViewBag.Sizes = ProductViewModel.Sizes.Union(dbSizes, StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        ViewBag.Brands = ProductViewModel.Brands.Union(dbBrands, StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Builds a SKU as "{CATEGORY-PREFIX}-{4-digit sequence}", e.g. "CLO-0001", and
    /// retries with the next number on the rare chance of a collision, so Create()
    /// never has to trust a client-supplied SKU.
    /// </summary>
    private async Task<string> GenerateUniqueSkuAsync(string category)
    {
        var prefix = new string((category ?? "GEN")
            .Where(char.IsLetter)
            .Take(3)
            .ToArray()).ToUpperInvariant();
        if (prefix.Length == 0) prefix = "GEN";

        var existingSkus = await _context.Products
            .Where(p => p.SKU.StartsWith(prefix + "-"))
            .Select(p => p.SKU)
            .ToListAsync();
        var existingSet = existingSkus.ToHashSet();

        for (var attempt = existingSkus.Count + 1; ; attempt++)
        {
            var candidate = $"{prefix}-{attempt:D4}";
            if (!existingSet.Contains(candidate)) return candidate;
        }
    }
    private async Task LogAuditAsync(string action, string details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = action,
            Details = details
        });
        await _context.SaveChangesAsync();
    }
}