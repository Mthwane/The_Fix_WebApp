using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

public class ProductViewModel

{

    public int ProductId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000), Display(Name = "Description")]
    public string? Description { get; set; }

    // SKU is server-generated (see ProductsController.GenerateUniqueSkuAsync) and never
    // posted from the Create form. On Edit it is shown read-only, so no [Required]/length
    // validation is needed here - the server always supplies a valid value.
    [Display(Name = "SKU")]
    public string SKU { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    public string? Size { get; set; }

    [MaxLength(30)]
    public string? Color { get; set; }

    public string? Brand { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Cost price must be between R0.01 and R100,000.")]
    [Display(Name = "Cost Price")]
    public decimal CostPrice { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Selling price must be between R0.01 and R100,000.")]
    [SellingPriceNotBelowCost]
    [Display(Name = "Selling Price")]
    public decimal SellingPrice { get; set; }

    [Display(Name = "Product Image")]
    public string? ImageUrl { get; set; }

    [Range(0, 10000, ErrorMessage = "Stock quantity must be between 0 and 10,000.")]
    [Display(Name = "Stock Quantity")]
    public int StockQuantity { get; set; }

    [Range(0, 1000, ErrorMessage = "Low stock threshold must be between 0 and 1,000.")]
    [Display(Name = "Low Stock Threshold")]
    public int LowStockThreshold { get; set; } = 5;

    public bool IsActive { get; set; } = true;

    /// <summary>Fixed category list backing the Category dropdown (Create/Edit views).</summary>
    public static readonly string[] Categories = { "Clothing", "Shoes", "Accessories" };

    /// <summary>Fixed colour list backing the Colour dropdown (Create/Edit views).</summary>
    public static readonly string[] Colors =
    {
        "Black", "White", "Grey", "Red", "Blue", "Green", "Yellow", "Pink", "Brown", "Beige", "Multi"
    };
}

/// <summary>
/// Validates that selling price isn't set below cost price. Wired up as a custom
/// validation attribute so it shows next to the field like the other DataAnnotations checks.
/// </summary>
public class SellingPriceNotBelowCostAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var model = (ProductViewModel)context.ObjectInstance;
        if (model.SellingPrice < model.CostPrice)
            return new ValidationResult("Selling price cannot be lower than cost price.", new[] { context.MemberName! });
        return ValidationResult.Success;
    }
}

/// <summary>Search/filter criteria for the master catalogue view (US-02).</summary>
public class ProductFilterViewModel
{
    public string? SearchTerm { get; set; }
    public string? Category { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public bool? InStockOnly { get; set; }
}