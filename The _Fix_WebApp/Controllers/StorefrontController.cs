using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Admin tooling for the parts of the storefront that aren't a single product or department:
/// which products are curated onto the homepage "Trending This Week" section (with optional
/// per-card copy/image overrides), the homepage's own hero/Style Box text, and moderating
/// customer reviews. Grouped together here rather than split into three tiny controllers since
/// they're all "storefront curation" tasks an admin/manager does from the same mental model.
/// </summary>
[Authorize(Policy = Permissions.StorefrontManage)]
public class StorefrontController : Controller
{
    private readonly ApplicationDbContext _context;

    public StorefrontController(ApplicationDbContext context)
    {
        _context = context;
    }

    // ===================== Trending curation =====================

    // GET: /Storefront/Trending
    [HttpGet]
    public async Task<IActionResult> Trending()
    {
        var featured = await _context.FeaturedProducts
            .AsNoTracking()
            .Include(f => f.Product)
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync();

        // Product picker only offers active products not already featured, so the same
        // product can't accidentally be added twice.
        var featuredProductIds = featured.Select(f => f.ProductId).ToHashSet();
        ViewBag.AvailableProducts = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && !featuredProductIds.Contains(p.ProductId))
            .OrderBy(p => p.Name)
            .ToListAsync();

        return View(featured);
    }

    // POST: /Storefront/AddTrending
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTrending(int productId)
    {
        if (await _context.FeaturedProducts.AnyAsync(f => f.ProductId == productId))
        {
            this.ToastWarning("That product is already featured.");
            return RedirectToAction(nameof(Trending));
        }

        var product = await _context.Products.FindAsync(productId);
        if (product is null) return NotFound();

        var maxOrder = await _context.FeaturedProducts.AnyAsync()
            ? await _context.FeaturedProducts.MaxAsync(f => f.DisplayOrder)
            : 0;

        _context.FeaturedProducts.Add(new FeaturedProduct
        {
            ProductId = productId,
            DisplayOrder = maxOrder + 1,
            IsActive = true
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{product.Name}' added to Trending This Week.");
        return RedirectToAction(nameof(Trending));
    }

    // POST: /Storefront/UpdateTrending/5 - save per-card overrides + order for one featured pick.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTrending(int id, string? overrideTitle, string? overrideImageUrl, string? overrideBadge, int displayOrder, bool isActive)
    {
        var featured = await _context.FeaturedProducts.FindAsync(id);
        if (featured is null) return NotFound();

        featured.OverrideTitle = string.IsNullOrWhiteSpace(overrideTitle) ? null : overrideTitle.Trim();
        featured.OverrideImageUrl = string.IsNullOrWhiteSpace(overrideImageUrl) ? null : overrideImageUrl.Trim();
        featured.OverrideBadge = string.IsNullOrWhiteSpace(overrideBadge) ? null : overrideBadge.Trim();
        featured.DisplayOrder = displayOrder;
        featured.IsActive = isActive;
        await _context.SaveChangesAsync();

        this.ToastSuccess("Trending pick updated.");
        return RedirectToAction(nameof(Trending));
    }

    // POST: /Storefront/RemoveTrending/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTrending(int id)
    {
        var featured = await _context.FeaturedProducts.FindAsync(id);
        if (featured is null) return NotFound();

        _context.FeaturedProducts.Remove(featured);
        await _context.SaveChangesAsync();

        this.ToastSuccess("Removed from Trending This Week.");
        return RedirectToAction(nameof(Trending));
    }

    // ===================== Homepage content =====================

    // GET: /Storefront/Content
    [HttpGet]
    public async Task<IActionResult> Content()
    {
        var settings = await _context.SiteSettings.FirstOrDefaultAsync(s => s.Id == 1)
            ?? new SiteSettings ();
        return View(settings);
    }

    // POST: /Storefront/Content
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Content(SiteSettings model)
    {
        if (!ModelState.IsValid) return View(model);

        var settings = await _context.SiteSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings is null)
        {
            
            model.DateUpdated = DateTime.UtcNow;
            _context.SiteSettings.Add(model);
        }
        else
        {
            settings.HeroEyebrow = model.HeroEyebrow;
            settings.HeroHeadline = model.HeroHeadline;
            settings.HeroSubheadline = model.HeroSubheadline;
            settings.HeroImageUrl = model.HeroImageUrl;
            settings.HeroPrimaryCtaText = model.HeroPrimaryCtaText;
            settings.HeroSecondaryCtaText = model.HeroSecondaryCtaText;
            settings.StyleBoxHeadline = model.StyleBoxHeadline;
            settings.StyleBoxSubheadline = model.StyleBoxSubheadline;
            settings.DateUpdated = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        this.ToastSuccess("Homepage content updated.");
        return RedirectToAction(nameof(Content));
    }

    // ===================== Review moderation =====================

    // GET: /Storefront/Reviews
    // NOTE: deliberately no filter/search UI here yet (product, rating, date range) - flagged
    // as a backlog item. This is a flat, most-recent-first list with a Delete action only.
    [HttpGet]
    public async Task<IActionResult> Reviews()
    {
        var reviews = await _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
            .Include(r => r.Customer)
            .OrderByDescending(r => r.DateCreated)
            .Take(200)
            .ToListAsync();

        return View(reviews);
    }

    // POST: /Storefront/DeleteReview/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(int id)
    {
        var review = await _context.ProductReviews.FindAsync(id);
        if (review is null) return NotFound();

        var productId = review.ProductId;
        _context.ProductReviews.Remove(review);
        await _context.SaveChangesAsync();

        // Recalculate the product's denormalized rating fields now that a review is gone -
        // same logic as ShopController.SubmitReview, just running in reverse.
        var product = await _context.Products.FirstAsync(p => p.ProductId == productId);
        var remaining = await _context.ProductReviews.Where(r => r.ProductId == productId).ToListAsync();
        product.ReviewCount = remaining.Count;
        product.AverageRating = remaining.Count > 0 ? Math.Round(remaining.Average(r => r.Rating), 1) : 0;
        await _context.SaveChangesAsync();

        this.ToastSuccess("Review removed.");
        return RedirectToAction(nameof(Reviews));
    }
}
