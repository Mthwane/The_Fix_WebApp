using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>Additional gallery images for a product's storefront detail page (Product.ImageUrl remains the primary/fallback image).</summary>
public class ProductImage
{
    [Key]
    public int ProductImageId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required, MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>A customer's star rating + optional written review for a product. Product.AverageRating/ReviewCount are denormalized aggregates kept in sync by IReviewService whenever a row is added here.</summary>
public class ProductReview
{
    [Key]
    public int ProductReviewId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser Customer { get; set; } = null!;

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(2000)]
    public string? Comment { get; set; }

    /// <summary>True if the reviewer has a Delivered/Completed order containing this product - shown as a "Verified Purchase" badge.</summary>
    public bool IsVerifiedPurchase { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}

/// <summary>A product a customer has saved to their wishlist (the heart icon on every storefront card).</summary>
public class WishlistItem
{
    [Key]
    public int WishlistItemId { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser Customer { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
