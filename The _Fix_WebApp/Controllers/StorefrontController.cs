using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services.Images;
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
    public async Task<IActionResult> UpdateTrending(int id, string? overrideTitle, string? overrideImageUrl, IFormFile? overrideImageFile, string? overrideBadge, int displayOrder, bool isActive, [FromServices] IImageStorage imageStorage)
    {
        var featured = await _context.FeaturedProducts.FindAsync(id);
        if (featured is null) return NotFound();

        var oldImage = featured.OverrideImageUrl;
        if (overrideImageFile is { Length: > 0 })
        {
            var up = await imageStorage.UploadAsync(overrideImageFile, "trending");
            if (!up.Success)
            {
                this.ToastError(up.Error ?? "Image upload failed.");
                return RedirectToAction(nameof(Trending));
            }
            overrideImageUrl = up.Url; // a chosen file wins over the pasted URL
        }

        featured.OverrideTitle = string.IsNullOrWhiteSpace(overrideTitle) ? null : overrideTitle.Trim();
        featured.OverrideImageUrl = string.IsNullOrWhiteSpace(overrideImageUrl) ? null : overrideImageUrl.Trim();
        featured.OverrideBadge = string.IsNullOrWhiteSpace(overrideBadge) ? null : overrideBadge.Trim();
        featured.DisplayOrder = displayOrder;
        featured.IsActive = isActive;
        await _context.SaveChangesAsync();

        // Image replaced or cleared: remove the old stored file if nothing else uses it.
        if (!string.IsNullOrWhiteSpace(oldImage) && oldImage != featured.OverrideImageUrl)
            await DeleteIfUnusedAsync(oldImage, imageStorage);

        this.ToastSuccess("Trending pick updated.");
        return RedirectToAction(nameof(Trending));
    }

    // POST: /Storefront/RemoveTrending/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTrending(int id, [FromServices] IImageStorage imageStorage)
    {
        var featured = await _context.FeaturedProducts.FindAsync(id);
        if (featured is null) return NotFound();

        var oldImage = featured.OverrideImageUrl;
        _context.FeaturedProducts.Remove(featured);
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(oldImage))
            await DeleteIfUnusedAsync(oldImage, imageStorage);

        this.ToastSuccess("Removed from Trending This Week.");
        return RedirectToAction(nameof(Trending));
    }

    // ===================== Homepage content =====================

    // GET: /Storefront/Content
    [HttpGet]
    public async Task<IActionResult> Content()
    {
        var settings = await _context.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1) ?? new SiteSettings();
        settings.ApplyDefaults();   // show the live defaults in the boxes so admins edit real text, not empty fields
        await LoadDiscountChoicesAsync();
        return View(settings);
    }

    // POST: /Storefront/Content
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Content(SiteSettings model, IFormFile? heroFile, IFormFile? storyFile, [FromServices] IImageStorage imageStorage)
    {
        if (!ModelState.IsValid) { await LoadDiscountChoicesAsync(); return View(model); }

        var existingHero = (await _context.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1))?.HeroImageUrl;
        if (heroFile is { Length: > 0 })
        {
            var up = await imageStorage.UploadAsync(heroFile, "homepage");
            if (!up.Success)
            {
                ModelState.AddModelError(nameof(SiteSettings.HeroImageUrl), up.Error ?? "Image upload failed.");
                return View(model);
            }
            model.HeroImageUrl = up.Url; // a chosen file wins over the pasted URL
        }
        if (storyFile is { Length: > 0 })
        {
            var up = await imageStorage.UploadAsync(storyFile, "homepage");
            if (!up.Success)
            {
                ModelState.AddModelError(nameof(SiteSettings.StoryImageUrl), up.Error ?? "Image upload failed.");
                await LoadDiscountChoicesAsync();
                return View(model);
            }
            model.StoryImageUrl = up.Url;
        }

        var settings = await _context.SiteSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings is null)
        {
            settings = new SiteSettings { Id = 1 };
            _context.SiteSettings.Add(settings);
        }
        CopyEditableFields(model, settings);
        settings.DateUpdated = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(existingHero) && existingHero != model.HeroImageUrl)
            await DeleteIfUnusedAsync(existingHero, imageStorage);

        this.ToastSuccess("Homepage content updated.");
        return RedirectToAction(nameof(Content));
    }

    /// <summary>Copies every admin-editable homepage field. Blank text boxes are stored as null so the built-in default shows again.</summary>
    /// <summary>Every discount that could drive the promo strip (online-valid, not yet expired), newest first.</summary>
    private async Task LoadDiscountChoicesAsync()
    {
        var now = DateTime.UtcNow;
        ViewBag.Discounts = await _context.Discounts.AsNoTracking()
            .Where(d => d.IsActive && d.Channel != DiscountChannel.InStore && (d.ExpiresAt == null || d.ExpiresAt >= now))
            .OrderByDescending(d => d.StartsAt).Take(100).ToListAsync();
    }

    private static void CopyEditableFields(SiteSettings m, SiteSettings t)
    {
        static string? N(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        static string R(string? v, string d) => string.IsNullOrWhiteSpace(v) ? d : v.Trim();
        static string? Color(string? v) => System.Text.RegularExpressions.Regex.IsMatch(v ?? "", "^#[0-9A-Fa-f]{6}$") ? v : null;

        t.HideUtilityBar = m.HideUtilityBar; t.UtilityLeftText = N(m.UtilityLeftText); t.UtilityRightText = N(m.UtilityRightText);

        t.HidePromoStrip = m.HidePromoStrip; t.PromoStripText = N(m.PromoStripText);
        t.PromoStripLinkText = N(m.PromoStripLinkText); t.PromoStripLinkUrl = N(m.PromoStripLinkUrl);
        t.PromoStripBgColor = Color(m.PromoStripBgColor); t.PromoStripTextColor = Color(m.PromoStripTextColor);
        t.PromoStripDiscountId = m.PromoStripDiscountId; t.HidePromoStripWhenDiscountEnds = m.HidePromoStripWhenDiscountEnds;

        t.HeroEyebrow = R(m.HeroEyebrow, new SiteSettings().HeroEyebrow);
        t.HeroHeadline = R(m.HeroHeadline, new SiteSettings().HeroHeadline);
        t.HeroSubheadline = R(m.HeroSubheadline, new SiteSettings().HeroSubheadline);
        t.HeroImageUrl = N(m.HeroImageUrl);
        t.HeroPrimaryCtaText = R(m.HeroPrimaryCtaText, "Shop The Fix");
        t.HeroSecondaryCtaText = R(m.HeroSecondaryCtaText, "Explore Lookbook");
        t.HeroStatsText = N(m.HeroStatsText);

        t.HideFeatures = m.HideFeatures; t.FeaturesText = N(m.FeaturesText);

        t.DepartmentsEyebrow = N(m.DepartmentsEyebrow); t.DepartmentsHeadline = N(m.DepartmentsHeadline); t.DepartmentsSubtext = N(m.DepartmentsSubtext);
        t.TrendingEyebrow = N(m.TrendingEyebrow); t.TrendingHeadline = N(m.TrendingHeadline);

        t.HideStyleBox = m.HideStyleBox; t.StyleBoxEyebrow = N(m.StyleBoxEyebrow);
        t.StyleBoxHeadline = R(m.StyleBoxHeadline, new SiteSettings().StyleBoxHeadline);
        t.StyleBoxSubheadline = R(m.StyleBoxSubheadline, new SiteSettings().StyleBoxSubheadline);
        t.StyleBoxStepsText = N(m.StyleBoxStepsText); t.StyleBoxPrimaryCtaText = N(m.StyleBoxPrimaryCtaText); t.StyleBoxSecondaryCtaText = N(m.StyleBoxSecondaryCtaText);

        t.HideStory = m.HideStory; t.StoryEyebrow = N(m.StoryEyebrow); t.StoryHeadline = N(m.StoryHeadline); t.StoryBody = N(m.StoryBody);
        t.StoryImageUrl = N(m.StoryImageUrl); t.StoryChecklistText = N(m.StoryChecklistText); t.StoryCtaText = N(m.StoryCtaText);

        t.HideBoutique = m.HideBoutique; t.BoutiqueHeadline = N(m.BoutiqueHeadline); t.BoutiqueText = N(m.BoutiqueText);
        t.BoutiquePrimaryCtaText = N(m.BoutiquePrimaryCtaText); t.BoutiqueSecondaryCtaText = N(m.BoutiqueSecondaryCtaText);
    }

    /// <summary>Deletes a stored image only if no product, gallery row, department, trending card or homepage setting still uses it.</summary>
    private async Task DeleteIfUnusedAsync(string url, IImageStorage imageStorage)
    {
        var used = await _context.Products.AnyAsync(p => p.ImageUrl == url)
            || await _context.ProductImages.AnyAsync(i => i.ImageUrl == url)
            || await _context.Departments.AnyAsync(d => d.HeroImageUrl == url || d.TileImageUrl == url)
            || await _context.FeaturedProducts.AnyAsync(f => f.OverrideImageUrl == url)
            || await _context.SiteSettings.AnyAsync(s => s.HeroImageUrl == url || s.StoryImageUrl == url);
        if (!used) await imageStorage.DeleteAsync(url);
    }

    // ===================== Review moderation =====================

    private const int ReviewsPageSize = 10;

    // GET: /Storefront/Reviews?search=&rating=&verified=&photos=&from=&to=&sort=&page=
    // Moderation list: filter by product / customer / comment text, star rating, verified purchases, reviews with photos
    // and a date range; sort newest, oldest, lowest or highest rating. 10 per page. The filters ride along on every page
    // link and on the Delete button, so deleting a review returns you to the same filtered page.
    [HttpGet]
    public async Task<IActionResult> Reviews(string? search, int? rating, bool? verified, bool? photos, DateTime? from, DateTime? to, string? sort, int? page)
    {
        var query = _context.ProductReviews.AsNoTracking()
            .Include(r => r.Product)
            .Include(r => r.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.Product.Name.Contains(term)
                || r.Product.SKU.Contains(term)
                || (r.Customer != null && r.Customer.FullName.Contains(term))
                || (r.Comment != null && r.Comment.Contains(term)));
        }
        if (rating is >= 1 and <= 5) query = query.Where(r => r.Rating == rating);
        if (verified == true) query = query.Where(r => r.IsVerifiedPurchase);
        if (photos == true) query = query.Where(r => r.PhotoUrls != null && r.PhotoUrls != "");
        if (from.HasValue) query = query.Where(r => r.DateCreated >= from.Value.Date);
        if (to.HasValue) query = query.Where(r => r.DateCreated < to.Value.Date.AddDays(1));

        query = sort switch
        {
            "oldest" => query.OrderBy(r => r.DateCreated),
            "lowest" => query.OrderBy(r => r.Rating).ThenByDescending(r => r.DateCreated),
            "highest" => query.OrderByDescending(r => r.Rating).ThenByDescending(r => r.DateCreated),
            _ => query.OrderByDescending(r => r.DateCreated)
        };

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)ReviewsPageSize));
        var current = PagerModel.ClampPage(page, totalPages);

        var items = await query.Skip((current - 1) * ReviewsPageSize).Take(ReviewsPageSize).ToListAsync();

        // Headline numbers for the whole catalogue (not just the filtered page).
        var summary = await _context.ProductReviews.AsNoTracking()
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Avg = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync();

        var rv = new Dictionary<string, string?>
        {
            ["search"] = search,
            ["rating"] = rating?.ToString(),
            ["verified"] = verified == true ? "true" : null,
            ["photos"] = photos == true ? "true" : null,
            ["from"] = from?.ToString("yyyy-MM-dd"),
            ["to"] = to?.ToString("yyyy-MM-dd"),
            ["sort"] = sort
        };

        ViewBag.Search = search; ViewBag.Rating = rating; ViewBag.Verified = verified == true; ViewBag.Photos = photos == true;
        ViewBag.From = from; ViewBag.To = to; ViewBag.Sort = sort ?? "newest";
        ViewBag.TotalAll = summary?.Count ?? 0;
        ViewBag.AverageAll = summary?.Avg ?? 0d;
        ViewBag.TotalFiltered = total;
        ViewBag.Filters = rv;
        ViewBag.Pager = new PagerModel
        {
            Page = current, TotalPages = totalPages, TotalItems = total, PageSize = ReviewsPageSize,
            Action = nameof(Reviews), RouteValues = rv
        };

        return View(items);
    }

    // POST: /Storefront/DeleteReview/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(int id, string? returnQuery)
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
        // Back to the same filtered page (only ever a local query string - never an arbitrary URL).
        if (!string.IsNullOrEmpty(returnQuery) && returnQuery.StartsWith('?'))
            return LocalRedirect(Url.Action(nameof(Reviews)) + returnQuery);
        return RedirectToAction(nameof(Reviews));
    }
}
