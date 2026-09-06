using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>View model for Shop/Product/{id} - the storefront product detail page.</summary>
public class ProductDetailViewModel
{
    public Product Product { get; set; } = null!;
    public List<ProductImage> Images { get; set; } = new();
    public List<ProductReview> Reviews { get; set; } = new();
    public bool IsWishlisted { get; set; }
    public bool CanReview { get; set; } // signed in as a Customer who hasn't already reviewed this product
    public string? ReturnUrl { get; set; }
}
