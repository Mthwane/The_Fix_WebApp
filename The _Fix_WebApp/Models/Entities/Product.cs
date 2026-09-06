using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// A "style" - the shared name/description/brand/pricing shell for a product. As of the
/// variant rework, Product no longer carries its own Size, Colour, sellable SKU, or
/// StockQuantity - those all moved to ProductVariant (one row per Size/Colour combination,
/// each with its own SKU and stock count). Product.SKU is kept as the "style code" prefix
/// that every one of its variants' SKUs is built from (e.g. style code "CLO-0001" ->
/// variant SKUs "CLO-0001-M-BLK", "CLO-0001-L-BLK", ...).
/// </summary>
public class Product
{
    [Key]
    public int ProductId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Style code, e.g. "CLO-0001" - unique, server-generated, never a sellable SKU by itself anymore (see ProductVariant.SKU).</summary>
    [Required, MaxLength(50)]
    public string SKU { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty; // Clothing, Shoes, Accessories...

    [MaxLength(50)]
    public string? Brand { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SellingPrice { get; set; }

    /// <summary>Original price shown struck through when the style is on sale; null = not on sale.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? CompareAtPrice { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>Applied per-variant: a variant is flagged low-stock when its own StockQuantity falls at/below this.</summary>
    public int LowStockThreshold { get; set; } = 5;

    /// <summary>Soft-delete flag - phased-out styles are deactivated, not deleted.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime? DateUpdated { get; set; }

    // --- Storefront / merchandising fields (added for the department-page rebuild) ---
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    [MaxLength(50)]
    public string? SubCategory { get; set; } // e.g. "Trail & Running", "Studio Active"

    [MaxLength(50)]
    public string? Material { get; set; } // e.g. "Organic Cotton", "Tencel", "Hemp"

    [MaxLength(30)]
    public string? Fit { get; set; } // e.g. "Relaxed", "Tailored", "Oversized"

    [MaxLength(30)]
    public string? Badge { get; set; } // "Bestseller", "New Arrival", "Editor's Pick", "Member Exclusive"

    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }

    // --- Navigation ---
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
    public ICollection<WishlistItem> WishlistedBy { get; set; } = new List<WishlistItem>();

    [NotMapped]
    public bool IsOnSale => CompareAtPrice.HasValue && CompareAtPrice > SellingPrice;

    [NotMapped]
    public decimal? DiscountPercentage => IsOnSale
        ? Math.Round((1 - (SellingPrice / CompareAtPrice!.Value)) * 100, 0)
        : null;

    /// <summary>Sum of stock across every active variant - what "in stock" means for the style as a whole (e.g. on a product card before a size is picked).</summary>
    [NotMapped]
    public int TotalStockQuantity => Variants.Where(v => v.IsActive).Sum(v => v.StockQuantity);

    /// <summary>True if at least one active variant is at/below the threshold - drives the low-stock badge at the style level (Dashboard summary, catalogue list).</summary>
    [NotMapped]
    public bool HasLowStockVariant => Variants.Any(v => v.IsActive && v.StockQuantity <= LowStockThreshold);

    [NotMapped]
    public IEnumerable<string> DistinctSizes => Variants.Where(v => v.IsActive && v.Size != null).Select(v => v.Size!).Distinct();

    [NotMapped]
    public IEnumerable<string> DistinctColors => Variants.Where(v => v.IsActive && v.Color != null).Select(v => v.Color!).Distinct();
}
