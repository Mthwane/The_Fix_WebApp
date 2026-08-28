using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

public class ProductViewModel

{
    /// <summary>
    /// Shared pattern for the standardised attribute fields (Category, Size, Colour,
    /// Brand): letters, spaces, hyphens and apostrophes only - no digits, no other
    /// symbols. Keeps SKU generation (which pulls letters out of Category) predictable
    /// and stops accidental junk like "12" or "N/A" ending up in the dropdown lists.
    /// </summary>
    public const string AttributeWordPattern = @"^[A-Za-z]+(?:[ '\-][A-Za-z]+)*$";

    /// <summary>Same as <see cref="AttributeWordPattern"/> but also accepts an empty value, for the optional fields (Size/Colour/Brand aren't required).</summary>
    public const string OptionalAttributeWordPattern = @"^$|" + AttributeWordPattern;

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

    [Required(ErrorMessage = "Please choose or enter a category.")]
    [MaxLength(50)]
    [RegularExpression(AttributeWordPattern, ErrorMessage = "Category can only contain letters, spaces and hyphens - no numbers or symbols.")]
    public string Category { get; set; } = string.Empty;

    [MaxLength(20)]
    [RegularExpression(OptionalAttributeWordPattern, ErrorMessage = "Size can only contain letters (e.g. S, M, L, XL) - no numbers or symbols.")]
    public string? Size { get; set; }

    [MaxLength(30)]
    [RegularExpression(OptionalAttributeWordPattern, ErrorMessage = "Colour can only contain letters, spaces and hyphens - no numbers or symbols.")]
    public string? Color { get; set; }

    [MaxLength(50)]
    [RegularExpression(OptionalAttributeWordPattern, ErrorMessage = "Brand can only contain letters, spaces and hyphens - no numbers or symbols.")]
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

    /// <summary>Category is a closed list - exactly these 6, picked from a real dropdown (not free text). Edit this array to change the set.</summary>
    public static readonly string[] Categories = { "Clothing", "Shoes", "Accessories", "Outerwear", "Activewear", "Underwear" };

    /// <summary>Seed size list - Size stays a self-sustaining dropdown (free text + suggestions), unlike Category/Color.</summary>
    public static readonly string[] Sizes = { "XS", "S", "M", "L", "XL", "XXL", "One Size" };

    /// <summary>Colour is a closed list - exactly these 6, picked from a real dropdown (not free text). Edit this array to change the set.</summary>
    public static readonly string[] Colors = { "Black", "White", "Grey", "Navy", "Beige", "Red" };

    /// <summary>No fixed seed for Brand - the dropdown is built entirely from brands already used on existing products.</summary>
    public static readonly string[] Brands = Array.Empty<string>();
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