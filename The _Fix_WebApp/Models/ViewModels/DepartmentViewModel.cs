using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

public class DepartmentViewModel
{
    public int DepartmentId { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-safe identifier (e.g. "women") used in /Shop/Department/{slug} - must be
    /// unique, lowercase letters/numbers/hyphens only so it never produces a broken link.</summary>
    [Required, MaxLength(50)]
    [RegularExpression(@"^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug can only contain lowercase letters, numbers, and hyphens (e.g. \"women\", \"sale-outlet\").")]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    [Display(Name = "Hero Image URL")]
    public string? HeroImageUrl { get; set; }

    [MaxLength(200)]
    [Display(Name = "Hero Headline")]
    public string? HeroHeadline { get; set; }

    [MaxLength(500)]
    [Display(Name = "Hero Subheadline")]
    public string? HeroSubheadline { get; set; }

    [MaxLength(500)]
    [Display(Name = "Tile Image URL (homepage department portal card)")]
    public string? TileImageUrl { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<DepartmentSubCategoryInputViewModel> SubCategories { get; set; } = new();
}

public class DepartmentSubCategoryInputViewModel
{
    public int DepartmentSubCategoryId { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    [RegularExpression(@"^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug can only contain lowercase letters, numbers, and hyphens.")]
    public string Slug { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    /// <summary>Set on a row the admin clicked "Remove" on client-side - tells the server to
    /// delete this subcategory on save, same pattern as ProductVariantInputViewModel.Remove.</summary>
    public bool Remove { get; set; }
}
