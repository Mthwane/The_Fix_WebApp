using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

public class PricingSettingsViewModel
{
    public decimal DefaultMarkupPercentage { get; set; }
    public List<CategoryMarkupRow> CategoryRules { get; set; } = new();
}

/// <summary>One row per category in ProductViewModel.Categories (always all 6, whether or not
/// that category has its own CategoryPricingRule yet) - so the page always shows every
/// category's effective markup, never just the ones someone happened to configure.</summary>
public class CategoryMarkupRow
{
    public string Category { get; set; } = string.Empty;
    public CategoryPricingRule? Rule { get; set; }
    public bool HasOverride => Rule is not null;
}
