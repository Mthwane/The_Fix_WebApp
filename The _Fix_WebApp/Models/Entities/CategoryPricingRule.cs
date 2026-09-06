using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// A markup override for one specific product category (see ProductViewModel.Categories for
/// the closed list of 6). A category with no row here just falls back to
/// SiteSettings.DefaultMarkupPercentage - there's always an effective markup, never a
/// missing/undefined one, so ProductsController never has to guess.
/// </summary>
public class CategoryPricingRule
{
    [Key]
    public int CategoryPricingRuleId { get; set; }

    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    /// <summary>e.g. 60 means Selling Price = Cost Price x 1.60.</summary>
    [Range(0, 1000)]
    public decimal MarkupPercentage { get; set; }

    public DateTime DateUpdated { get; set; } = DateTime.UtcNow;
}
