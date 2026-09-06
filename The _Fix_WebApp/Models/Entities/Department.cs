using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Storefront department (Women / Men / Kids / Footwear / Accessories / Sale &amp; Outlet) -
/// separate from Product.Category (Clothing/Shoes/Accessories/...), since a department is
/// "who it's for" while Category is "what kind of item it is". Drives the top nav and the
/// "Curated Department Portals" tiles on the landing page.
/// </summary>
public class Department
{
    [Key]
    public int DepartmentId { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Slug { get; set; } = string.Empty; // "women", "men", "kids", "footwear", "accessories", "sale"

    [MaxLength(500)]
    public string? HeroImageUrl { get; set; }

    [MaxLength(200)]
    public string? HeroHeadline { get; set; }

    [MaxLength(500)]
    public string? HeroSubheadline { get; set; }

    [MaxLength(500)]
    public string? TileImageUrl { get; set; } // landing-page department tile image

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<DepartmentSubCategory> SubCategories { get; set; } = new List<DepartmentSubCategory>();
}

/// <summary>Pill/tab navigation within a department, e.g. Footwear's "Trail & Running / Lifestyle Sneakers / ...".</summary>
public class DepartmentSubCategory
{
    [Key]
    public int DepartmentSubCategoryId { get; set; }

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Slug { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}
