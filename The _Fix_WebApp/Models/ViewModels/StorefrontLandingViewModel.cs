using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>View model for Home/Index - the public storefront landing page (matches the
/// Figma "Main Storefront Landing Page" design). Anonymous visitors and logged-in
/// customers both land here; only staff get redirected straight to their Dashboard.</summary>
public class StorefrontLandingViewModel
{
    public List<TrendingCardViewModel> Trending { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public SiteSettings SiteSettings { get; set; } = new();
}

/// <summary>One card in the homepage Trending grid. Wraps a Product with the admin's
/// per-card overrides (see FeaturedProduct) already resolved, so the view never has to
/// know whether a value came from the curated pick or the product's own fields.</summary>
public class TrendingCardViewModel
{
    public Product Product { get; set; } = null!;
    public string DisplayTitle { get; set; } = string.Empty;
    public string? DisplayImageUrl { get; set; }
    public string? DisplayBadge { get; set; }
}
