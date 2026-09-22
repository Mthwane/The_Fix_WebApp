using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// An admin-curated pick for the homepage "Trending This Week" section. Deliberately
/// separate from Product itself so a merchandiser can feature a product with different
/// marketing copy/imagery on the homepage than what shows on the product's own detail page
/// (e.g. a seasonal badge, a styled hero shot instead of the flat product photo) without
/// touching the underlying catalogue data. If no rows exist, HomeController falls back to
/// an algorithmic pick (top-rated/most-reviewed) so the section is never empty by default.
/// </summary>
public class FeaturedProduct
{
    [Key]
    public int FeaturedProductId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Overrides - null means "fall back to the product's own field" (see FeaturedProductCardViewModel).
    [MaxLength(150)]
    public string? OverrideTitle { get; set; }

    [MaxLength(500)]
    public string? OverrideImageUrl { get; set; }

    [MaxLength(30)]
    public string? OverrideBadge { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
