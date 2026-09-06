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

[Authorize(Policy = Permissions.ProductsManage)]
public class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IInventoryService _inventoryService;

    public ProductsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IInventoryService inventoryService)
    {
        _context = context;
        _userManager = userManager;
        _inventoryService = inventoryService;
    }

    // GET: /Products/LowStock - dedicated restock queue, now one row per low SIZE/COLOUR
    // rather than one row per style, since two variants of the same style can be in very
    // different stock positions (e.g. "M/Black" low, "L/Black" fine).
    [HttpGet]
    public async Task<IActionResult> LowStock()
    {
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product.IsActive && v.StockQuantity <= v.Product.LowStockThreshold)
            .OrderBy(v => v.StockQuantity)
            .ToListAsync();

        return View(variants);
    }

    // POST: /Products/QuickRestock - bumps a single variant's stock by a manager-entered
    // quantity. Goes through IInventoryService so it's logged in InventoryTransactions like
    // any other stock movement, not a silent direct edit.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickRestock(int variantId, int quantity)
    {
        if (quantity <= 0)
        {
            this.ToastError("Enter a quantity greater than zero.");
            return RedirectToAction(nameof(LowStock));
        }

        var variant = await _context.ProductVariants.AsNoTracking().Include(v => v.Product).FirstOrDefaultAsync(v => v.ProductVariantId == variantId);
        if (variant is null) return NotFound();

        await _inventoryService.IncrementStockAsync(variantId, quantity, InventoryChangeReason.PurchaseOrderReceived);
        await LogAuditAsync("VariantRestocked", $"Restocked {variant.Product.Name} ({variant.Size}/{variant.Color}) by {quantity} unit(s).");

        this.ToastSuccess($"Added {quantity} unit(s) to {variant.Product.Name} ({variant.Size}/{variant.Color}).");
        return RedirectToAction(nameof(LowStock));
    }

    // POST: /Products/BatchReplenish - restocks every currently-low variant in one pass,
    // bringing each up to (threshold + 5) as a sensible buffer above the alert line. One
    // round trip via IncrementStockBatchAsync rather than looping single restocks.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchReplenish()
    {
        var lowStockVariants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product.IsActive && v.StockQuantity <= v.Product.LowStockThreshold)
            .ToListAsync();

        if (lowStockVariants.Count == 0)
        {
            this.ToastSuccess("Nothing to replenish - no variants are currently below threshold.");
            return RedirectToAction(nameof(LowStock));
        }

        var lines = lowStockVariants
            .Select(v => (VariantId: v.ProductVariantId, Quantity: Math.Max(1, v.Product.LowStockThreshold + 5 - v.StockQuantity)))
            .ToList();

        await _inventoryService.IncrementStockBatchAsync(lines, InventoryChangeReason.PurchaseOrderReceived);
        await LogAuditAsync("BatchReplenish", $"Batch-replenished {lines.Count} variant(s) up to threshold+5 buffer.");

        this.ToastSuccess($"Replenished {lines.Count} SKU(s) - each brought up to its threshold plus a 5-unit buffer.");
        return RedirectToAction(nameof(LowStock));
    }
    // Master catalogue with search/filter (US-02). Size/Colour filters now match styles
    // that have AT LEAST ONE active variant with that size/colour.
    [HttpGet]
    public async Task<IActionResult> Index(ProductFilterViewModel filter)
    {
        var query = _context.Products.AsNoTracking().Include(p => p.Variants).Where(p => p.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            query = query.Where(p =>
                p.Name.Contains(filter.SearchTerm) ||
                p.SKU.Contains(filter.SearchTerm) ||
                p.Variants.Any(v => v.SKU.Contains(filter.SearchTerm)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
            query = query.Where(p => p.Category == filter.Category);

        if (!string.IsNullOrWhiteSpace(filter.Size))
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Size == filter.Size));

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Color == filter.Color));

        if (filter.InStockOnly == true)
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.StockQuantity > 0));

        var products = await query.OrderBy(p => p.Name).ToListAsync();

        ViewBag.Filter = filter;
        return View(products);
    }

    // GET: /Products/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        // Start with one blank variant row so the form isn't empty - a style must have
        // at least one sellable size/colour to be created.
        return View(new ProductViewModel { Variants = { new ProductVariantInputViewModel() } });
    }

    // POST: /Products/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductViewModel model)
    {
        // Style code is never taken from the posted form - it's always server-generated,
        // and so is every variant's own SKU.
        ModelState.Remove(nameof(ProductViewModel.SKU));
        for (var i = 0; i < model.Variants.Count; i++)
            ModelState.Remove($"{nameof(model.Variants)}[{i}].{nameof(ProductVariantInputViewModel.SKU)}");

        ValidateClosedLists(model);

        // Auto-calculate takes over the field entirely - whatever the client posted for
        // SellingPrice (even a live JS preview) is never trusted as the real number, same
        // reasoning as the POS/checkout price-trust issue: the server recomputes it.
        if (model.AutoCalculatePrice)
        {
            ModelState.Remove(nameof(ProductViewModel.SellingPrice));
            model.SellingPrice = await CalculateAutoSellingPriceAsync(model.CostPrice, model.Category);
        }

        var activeVariantRows = model.Variants.Where(v => !v.Remove).ToList();
        if (activeVariantRows.Count == 0)
            ModelState.AddModelError(string.Empty, "Add at least one size/colour before saving.");

        // A style can't have the same size/colour typed in twice on the same form.
        var duplicateKeys = activeVariantRows
            .GroupBy(v => ((v.Size ?? "").Trim().ToUpperInvariant(), (v.Color ?? "").Trim().ToUpperInvariant()))
            .Where(g => g.Count() > 1);
        if (duplicateKeys.Any())
            ModelState.AddModelError(string.Empty, "Each size/colour combination can only be listed once.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        var styleCode = await GenerateUniqueStyleCodeAsync(model.Category);

        var product = new Product
        {
            Name = model.Name,
            Description = model.Description,
            SKU = styleCode,
            Category = model.Category,
            Brand = model.Brand,
            CostPrice = model.CostPrice,
            SellingPrice = model.SellingPrice,
            CompareAtPrice = model.CompareAtPrice,
            ImageUrl = model.ImageUrl,
            LowStockThreshold = model.LowStockThreshold,
            SubCategory = model.SubCategory,
            Material = model.Material,
            Fit = model.Fit,
            Badge = model.Badge,
            DepartmentId = model.DepartmentId,
            IsActive = true
        };

        foreach (var row in activeVariantRows)
        {
            product.Variants.Add(new ProductVariant
            {
                SKU = await GenerateUniqueVariantSkuAsync(styleCode, row.Size, row.Color),
                Size = row.Size,
                Color = row.Color,
                ColorHex = row.ColorHex,
                StockQuantity = row.StockQuantity,
                PriceOverride = row.PriceOverride,
                IsActive = true
            });
        }

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        await LogAuditAsync("ProductCreated", $"Added product '{product.Name}' (style {product.SKU}) with {product.Variants.Count} variant(s).");
        this.ToastSuccess($"'{product.Name}' was added to the catalogue with {product.Variants.Count} size/colour option(s).");

        return RedirectToAction(nameof(Index));
    }

    // GET: /Products/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.ProductId == id);
        if (product is null) return NotFound();

        await PopulateDropdownsAsync();

        var model = new ProductViewModel
        {
            ProductId = product.ProductId,
            Name = product.Name,
            Description = product.Description,
            SKU = product.SKU,
            Category = product.Category,
            Brand = product.Brand,
            CostPrice = product.CostPrice,
            SellingPrice = product.SellingPrice,
            // Off by default on Edit (unlike Create) - an existing price may have been set
            // deliberately (a promo, a rounding choice); staff opt back into auto-calculate
            // rather than having it silently recompute on every open of this form.
            AutoCalculatePrice = false,
            CompareAtPrice = product.CompareAtPrice,
            ImageUrl = product.ImageUrl,
            LowStockThreshold = product.LowStockThreshold,
            SubCategory = product.SubCategory,
            Material = product.Material,
            Fit = product.Fit,
            Badge = product.Badge,
            DepartmentId = product.DepartmentId,
            IsActive = product.IsActive,
            Variants = product.Variants
                .OrderBy(v => v.Size).ThenBy(v => v.Color)
                .Select(v => new ProductVariantInputViewModel
                {
                    ProductVariantId = v.ProductVariantId,
                    Size = v.Size,
                    Color = v.Color,
                    ColorHex = v.ColorHex,
                    StockQuantity = v.StockQuantity,
                    SKU = v.SKU,
                    PriceOverride = v.PriceOverride,
                    IsActive = v.IsActive
                }).ToList()
        };

        return View(model);
    }

    // POST: /Products/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductViewModel model)
    {
        if (id != model.ProductId) return BadRequest();

        // Style code is read-only on Edit - never overwrite it from the posted form.
        // Existing variants' SKUs are likewise fixed once assigned.
        ModelState.Remove(nameof(ProductViewModel.SKU));
        for (var i = 0; i < model.Variants.Count; i++)
            ModelState.Remove($"{nameof(model.Variants)}[{i}].{nameof(ProductVariantInputViewModel.SKU)}");

        ValidateClosedLists(model);

        if (model.AutoCalculatePrice)
        {
            ModelState.Remove(nameof(ProductViewModel.SellingPrice));
            model.SellingPrice = await CalculateAutoSellingPriceAsync(model.CostPrice, model.Category);
        }

        var product = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.ProductId == id);
        if (product is null) return NotFound();

        var remainingRows = model.Variants.Where(v => !v.Remove).ToList();
        if (remainingRows.Count == 0)
            ModelState.AddModelError(string.Empty, "A style must keep at least one active size/colour - deactivate the whole product instead if it's being discontinued.");

        var duplicateKeys = remainingRows
            .GroupBy(v => ((v.Size ?? "").Trim().ToUpperInvariant(), (v.Color ?? "").Trim().ToUpperInvariant()))
            .Where(g => g.Count() > 1);
        if (duplicateKeys.Any())
            ModelState.AddModelError(string.Empty, "Each size/colour combination can only be listed once.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(model);
        }

        product.Name = model.Name;
        product.Description = model.Description;
        // product.SKU (style code) intentionally left unchanged - it's fixed at creation time.
        product.Category = model.Category;
        product.Brand = model.Brand;
        product.CostPrice = model.CostPrice;
        product.SellingPrice = model.SellingPrice;
        product.CompareAtPrice = model.CompareAtPrice;
        product.ImageUrl = model.ImageUrl;
        product.LowStockThreshold = model.LowStockThreshold;
        product.SubCategory = model.SubCategory;
        product.Material = model.Material;
        product.Fit = model.Fit;
        product.Badge = model.Badge;
        product.DepartmentId = model.DepartmentId;
        product.DateUpdated = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Variant-by-variant reconciliation:
        //  - rows with ProductVariantId == 0 are brand new sizes/colours added on this edit
        //  - rows marked Remove that already exist are soft-deactivated, never hard-deleted
        //    (a variant may already have sales history against it)
        //  - rows marked Remove that were never saved (Id == 0) are simply dropped
        //  - everything else has its non-stock fields updated directly, and its stock
        //    delta routed through IInventoryService so it lands in InventoryTransactions
        //    exactly like the old top-level StockQuantity field used to (ManualAdjustment)
        foreach (var row in model.Variants)
        {
            if (row.ProductVariantId == 0)
            {
                if (row.Remove) continue; // added then immediately removed on the same submit - ignore

                product.Variants.Add(new ProductVariant
                {
                    SKU = await GenerateUniqueVariantSkuAsync(product.SKU, row.Size, row.Color),
                    Size = row.Size,
                    Color = row.Color,
                    ColorHex = row.ColorHex,
                    StockQuantity = row.StockQuantity,
                    PriceOverride = row.PriceOverride,
                    IsActive = true
                });
                continue;
            }

            var variant = product.Variants.FirstOrDefault(v => v.ProductVariantId == row.ProductVariantId);
            if (variant is null) continue; // stale id from a concurrent edit - skip rather than throw

            if (row.Remove)
            {
                variant.IsActive = false;
                variant.DateUpdated = DateTime.UtcNow;
                continue;
            }

            variant.Size = row.Size;
            variant.Color = row.Color;
            variant.ColorHex = row.ColorHex;
            variant.PriceOverride = row.PriceOverride;
            variant.IsActive = row.IsActive;
            variant.DateUpdated = DateTime.UtcNow;

            var stockDelta = row.StockQuantity - variant.StockQuantity;
            if (stockDelta > 0)
                await _inventoryService.IncrementStockAsync(variant.ProductVariantId, stockDelta, InventoryChangeReason.ManualAdjustment);
            else if (stockDelta < 0)
                await _inventoryService.DecrementStockAsync(variant.ProductVariantId, -stockDelta, InventoryChangeReason.ManualAdjustment);
        }

        await _context.SaveChangesAsync();

        await LogAuditAsync("ProductUpdated", $"Updated product '{product.Name}' (style {product.SKU}).");
        this.ToastSuccess($"'{product.Name}' was updated.");

        return RedirectToAction(nameof(Index));
    }

    // POST: /Products/Deactivate/5 - soft delete, never a hard DELETE. Deactivating a style
    // deactivates every one of its variants too, so it can no longer be scanned or browsed,
    // while every historical order line and inventory transaction against it is preserved.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var product = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.ProductId == id);
        if (product is null) return NotFound();

        product.IsActive = false;
        product.DateUpdated = DateTime.UtcNow;
        foreach (var variant in product.Variants)
        {
            variant.IsActive = false;
            variant.DateUpdated = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();

        await LogAuditAsync("ProductDeactivated", $"Deactivated product '{product.Name}' (style {product.SKU}) and its {product.Variants.Count} variant(s).");
        this.ToastSuccess($"'{product.Name}' was deactivated.");

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Category and Colour are a closed list on a real &lt;select&gt;, but nothing stops a
    /// crafted POST from sending a value outside that list - reject it here rather than
    /// trusting the browser to have enforced it. Colour is now checked per variant row.
    /// </summary>
    // Category rule takes priority over the store-wide default; there's always an effective
    // markup (PricingSettings is seeded on first run - see Program.cs), so this never has to
    // guess or fall back to a hardcoded number buried in this controller.
    private async Task<decimal> CalculateAutoSellingPriceAsync(decimal costPrice, string category)
    {
        var categoryRule = await _context.CategoryPricingRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Category == category);

        var markupPercentage = categoryRule?.MarkupPercentage
            ?? (await _context.PricingSettings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1))?.DefaultMarkupPercentage
            ?? 60; // last-resort fallback if PricingSettings somehow hasn't been seeded yet

        var calculated = Math.Round(costPrice * (1 + markupPercentage / 100m), 2);
        return Math.Max(calculated, costPrice); // never let a 0% (or misconfigured negative) markup price below cost
    }

    private void ValidateClosedLists(ProductViewModel model)
    {
        if (!ProductViewModel.Categories.Contains(model.Category))
            ModelState.AddModelError(nameof(model.Category), "Please choose a category from the list.");

        for (var i = 0; i < model.Variants.Count; i++)
        {
            var row = model.Variants[i];
            if (row.Remove) continue;
            if (!string.IsNullOrEmpty(row.Color) && !ProductViewModel.Colors.Contains(row.Color))
                ModelState.AddModelError($"{nameof(model.Variants)}[{i}].{nameof(row.Color)}", "Please choose a colour from the list.");
        }
    }

    /// <summary>
    /// Builds the attribute dropdowns. Category and Colour are a closed list - always
    /// exactly ProductViewModel.Categories / .Colors, picked from a real &lt;select&gt;, so no
    /// junk value typed on one product can ever leak into another product's dropdown. Size
    /// and Brand stay "self-sustaining": the seed list unioned with whatever's already used
    /// on existing products/variants, so typing a new one there still works itself into
    /// future suggestions without a separate admin screen.
    /// </summary>
    private async Task PopulateDropdownsAsync()
    {
        ViewBag.Categories = ProductViewModel.Categories.ToList();
        ViewBag.Colors = ProductViewModel.Colors.ToList();

        var dbSizes = await _context.ProductVariants.AsNoTracking()
            .Where(v => v.Size != null && v.Size != "").Select(v => v.Size!).Distinct().ToListAsync();
        var dbBrands = await _context.Products.AsNoTracking()
            .Where(p => p.Brand != null && p.Brand != "").Select(p => p.Brand!).Distinct().ToListAsync();

        ViewBag.Sizes = ProductViewModel.Sizes.Union(dbSizes, StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        ViewBag.Brands = ProductViewModel.Brands.Union(dbBrands, StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();

        ViewBag.Departments = await _context.Departments.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.DisplayOrder).ToListAsync();

        // For the "Auto-calculate from markup" live preview on the form - category -> % map,
        // falling back to the store default for any category with no override.
        var defaultMarkup = (await _context.PricingSettings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1))?.DefaultMarkupPercentage ?? 60;
        var categoryRules = await _context.CategoryPricingRules.AsNoTracking().ToDictionaryAsync(r => r.Category, r => r.MarkupPercentage);
        ViewBag.DefaultMarkupPercentage = defaultMarkup;
        ViewBag.MarkupByCategory = ProductViewModel.Categories.ToDictionary(c => c, c => categoryRules.TryGetValue(c, out var m) ? m : defaultMarkup);
    }

    /// <summary>
    /// Builds a style code as "{CATEGORY-PREFIX}-{4-digit sequence}", e.g. "CLO-0001", and
    /// retries with the next number on the rare chance of a collision, so Create() never has
    /// to trust a client-supplied code. Every variant of this style then suffixes its own
    /// SKU onto this same code (see GenerateUniqueVariantSkuAsync).
    /// </summary>
    private async Task<string> GenerateUniqueStyleCodeAsync(string category)
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

    /// <summary>
    /// Builds a variant SKU as "{styleCode}-{SIZECODE}-{COLORCODE}", e.g.
    /// "CLO-0001-M-BLK", and falls back to an incrementing numeric suffix if that exact
    /// combination is somehow already taken (defensive - shouldn't happen given the
    /// per-style size/colour uniqueness check in Create/Edit).
    /// </summary>
    private async Task<string> GenerateUniqueVariantSkuAsync(string styleCode, string? size, string? color)
    {
        string Code(string? value, int length) =>
            string.IsNullOrWhiteSpace(value)
                ? "OS"
                : new string(value.Where(char.IsLetterOrDigit).Take(length).ToArray()).ToUpperInvariant();

        var baseCandidate = $"{styleCode}-{Code(size, 3)}-{Code(color, 3)}";

        var existing = await _context.ProductVariants
            .Where(v => v.SKU.StartsWith(baseCandidate))
            .Select(v => v.SKU)
            .ToListAsync();
        var existingSet = existing.ToHashSet();

        if (!existingSet.Contains(baseCandidate)) return baseCandidate;

        for (var attempt = 2; ; attempt++)
        {
            var candidate = $"{baseCandidate}-{attempt}";
            if (!existingSet.Contains(candidate)) return candidate;
        }
    }

    // GET: /Products/Images/5 - gallery management for a product's detail page.
    // Product.ImageUrl itself stays the card/fallback image; these are additional gallery shots.
    [HttpGet]
    public async Task<IActionResult> Images(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null) return NotFound();

        var images = await _context.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == id)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync();

        ViewBag.Product = product;
        return View(images);
    }

    // POST: /Products/AddImage
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddImage(int productId, string imageUrl, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            this.ToastError("Enter an image URL first.");
            return RedirectToAction(nameof(Images), new { id = productId });
        }

        if (isPrimary)
        {
            // Only one primary image per product - clear the flag off any existing one.
            var currentPrimary = await _context.ProductImages.Where(i => i.ProductId == productId && i.IsPrimary).ToListAsync();
            foreach (var img in currentPrimary) img.IsPrimary = false;
        }

        var maxOrder = await _context.ProductImages.Where(i => i.ProductId == productId).AnyAsync()
            ? await _context.ProductImages.Where(i => i.ProductId == productId).MaxAsync(i => i.DisplayOrder)
            : 0;

        _context.ProductImages.Add(new ProductImage
        {
            ProductId = productId,
            ImageUrl = imageUrl.Trim(),
            IsPrimary = isPrimary,
            DisplayOrder = maxOrder + 1
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess("Image added to gallery.");
        return RedirectToAction(nameof(Images), new { id = productId });
    }

    // POST: /Products/SetPrimaryImage/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrimaryImage(int id)
    {
        var image = await _context.ProductImages.FindAsync(id);
        if (image is null) return NotFound();

        var siblings = await _context.ProductImages.Where(i => i.ProductId == image.ProductId).ToListAsync();
        foreach (var img in siblings) img.IsPrimary = img.ProductImageId == id;
        await _context.SaveChangesAsync();

        this.ToastSuccess("Primary image updated.");
        return RedirectToAction(nameof(Images), new { id = image.ProductId });
    }

    // POST: /Products/RemoveImage/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveImage(int id)
    {
        var image = await _context.ProductImages.FindAsync(id);
        if (image is null) return NotFound();

        var productId = image.ProductId;
        _context.ProductImages.Remove(image);
        await _context.SaveChangesAsync();

        this.ToastSuccess("Image removed from gallery.");
        return RedirectToAction(nameof(Images), new { id = productId });
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
