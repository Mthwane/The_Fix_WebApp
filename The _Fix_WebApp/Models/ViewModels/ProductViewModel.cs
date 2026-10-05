using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>
/// A "style" - Size/Colour/Stock now live on the child ProductVariant rows (see
/// ProductVariantInputViewModel below), since a single style can be sold in many size/colour
/// combinations, each with its own SKU and stock count.
/// </summary>
public class ProductViewModel
{
    /// <summary>
    /// For Category (a closed dropdown of known words): letters, spaces, hyphens and
    /// apostrophes only.
    /// </summary>
    public const string AttributeWordPattern = @"^[A-Za-z]+(?:[ '\-][A-Za-z]+)*$";

    /// <summary>Same as <see cref="AttributeWordPattern"/> but also accepts an empty value, for optional fields.</summary>
    public const string OptionalAttributeWordPattern = @"^$|" + AttributeWordPattern;

    /// <summary>
    /// For Size: deliberately permissive, since real-world sizes are everything from letter
    /// sizes (S, M, L, XL) to pure numbers (shoe sizes like "7", "9.5") to combinations
    /// ("UK 7", "34x30", "One Size"). Just blocks obviously-wrong input like stray symbols.
    /// </summary>
    public const string OptionalSizePattern = @"^$|^[A-Za-z0-9](?:[A-Za-z0-9 ./x-]*[A-Za-z0-9])?$";

    public int ProductId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please add a description.")]
    [MaxLength(1000), Display(Name = "Description")]
    public string? Description { get; set; }

    // SKU is the style code, server-generated (see ProductsController.GenerateUniqueStyleCodeAsync)
    // and never posted from the Create form. Each variant's own SKU is built from this plus its
    // size/colour (see ProductsController.GenerateUniqueVariantSkuAsync).
    [Display(Name = "Style Code")]
    public string SKU { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please choose or enter a category.")]
    [MaxLength(50)]
    [RegularExpression(AttributeWordPattern, ErrorMessage = "Category can only contain letters, spaces and hyphens - no numbers or symbols.")]
    public string Category { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a brand.")]
    [MaxLength(50)]
    [Display(Name = "Brand")]
    public string? Brand { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Cost price must be between R0.01 and R100,000.")]
    [Display(Name = "Cost Price")]
    public decimal CostPrice { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Selling price must be between R0.01 and R100,000.")]
    [SellingPriceNotBelowCost]
    [Display(Name = "Selling Price")]
    public decimal SellingPrice { get; set; }

    /// <summary>When true, SellingPrice is ignored and recalculated server-side from CostPrice
    /// using the category's markup rule (falling back to the store default) - see
    /// ProductsController.CalculateAutoSellingPriceAsync. Never trust a client-computed
    /// preview value as the real number, same reasoning as the POS/checkout price-trust fix.</summary>
    [Display(Name = "Auto-calculate from markup")]
    public bool AutoCalculatePrice { get; set; } = true;

    /// <summary>Optional - shown struck through the SellingPrice on the storefront when set and higher than SellingPrice.</summary>
    [Range(0, 100000, ErrorMessage = "Compare-at price must be between R0 and R100,000.")]
    [Display(Name = "Compare-at Price (optional, for sale pricing)")]
    public decimal? CompareAtPrice { get; set; }

    [Display(Name = "Product Image")]
    public string? ImageUrl { get; set; }

    [Range(0, 1000, ErrorMessage = "Low stock threshold must be between 0 and 1,000.")]
    [Display(Name = "Low Stock Threshold (applies to every size/colour)")]
    public int LowStockThreshold { get; set; } = 5;

    public bool IsActive { get; set; } = true;

    // --- Storefront / merchandising (sub-category, material, fit and badge stay optional) ---
    [MaxLength(50)]
    public string? SubCategory { get; set; }

    [MaxLength(50)]
    public string? Material { get; set; }

    [MaxLength(30)]
    public string? Fit { get; set; }

    [MaxLength(30)]
    public string? Badge { get; set; }

    [Required(ErrorMessage = "Please choose a department.")]
    [Display(Name = "Department")]
    public int? DepartmentId { get; set; }

    /// <summary>Which supplier this style is bought from. Required so every product can be restocked through a purchase order.</summary>
    [Required(ErrorMessage = "Please choose a supplier.")]
    [Display(Name = "Supplier")]
    public int? SupplierId { get; set; }

    /// <summary>
    /// Every size/colour this style is sold in. Create requires at least one; Edit lets staff
    /// add new variants, adjust stock (routed through IInventoryService as a
    /// ManualAdjustment, exactly like the old top-level StockQuantity field used to be), or
    /// deactivate a discontinued size/colour without losing its sales history.
    /// </summary>
    public List<ProductVariantInputViewModel> Variants { get; set; } = new();

    /// <summary>Category is a closed list - exactly these 6, picked from a real dropdown (not free text). Edit this array to change the set.</summary>
    public static readonly string[] Categories = { "Clothing", "Shoes", "Accessories", "Outerwear", "Activewear", "Underwear" };

    /// <summary>Seed size list - offered as suggestions when adding a variant row (free text + datalist, same UX the old top-level Size field used).</summary>
    public static readonly string[] Sizes = { "XS", "S", "M", "L", "XL", "XXL", "One Size" };

    // ---- Footwear sizing: stored and shown as "UK 7" everywhere in the app ----
    /// <summary>The category whose variants use the fixed UK shoe-size list.</summary>
    public const string FootwearCategory = "Shoes";

    /// <summary>The fixed UK size dropdown for footwear (UK 1 - UK 15). Staff can still add a size outside this list
    /// (e.g. a half size "UK 7.5") through the "Add custom size" option - see wwwroot/js/variant-sizes.js.</summary>
    public static readonly string[] FootwearSizes = Enumerable.Range(1, 15).Select(n => $"UK {n}").ToArray();

    /// <summary>"UK 7" or "UK 7.5" - the only shape a footwear size may take once saved.</summary>
    public const string FootwearSizePattern = @"^UK (?:[1-9]|1[0-9]|20)(?:\.5)?$";

    public static bool IsFootwear(string? category) =>
        string.Equals(category, FootwearCategory, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns "7", "uk7", "UK-7", "uk 7.5" into "UK 7" / "UK 7.5". Anything unrecognised comes back trimmed and
    /// unchanged so validation can reject it with a clear message. Null/blank stays null.</summary>
    public static string? NormaliseFootwearSize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^(?:UK[\s\-]*)?(\d{1,2}(?:\.5)?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? $"UK {m.Groups[1].Value}" : t;
    }

    /// <summary>Display helper for any place that shows a size next to its category: footwear sizes always read "UK n",
    /// even for older rows saved as a bare number.</summary>
    public static string? DisplaySize(string? category, string? size) =>
        IsFootwear(category) ? NormaliseFootwearSize(size) : size;

    /// <summary>Colour name -> hex. The dropdown is built from these keys, and picking a colour auto-fills the swatch hex.</summary>
    public static readonly Dictionary<string, string> ColorHexMap = new()
    {
        ["Black"] = "#000000", ["White"] = "#FFFFFF", ["Grey"] = "#808080", ["Charcoal"] = "#36454F",
        ["Silver"] = "#C0C0C0", ["Navy"] = "#1F2A44", ["Blue"] = "#1E5BC6", ["Light Blue"] = "#9CC9F0",
        ["Teal"] = "#128C8C", ["Turquoise"] = "#30D5C8", ["Green"] = "#2E8B57", ["Olive"] = "#6B6B2F",
        ["Khaki"] = "#C3B091", ["Mint"] = "#B8E6CF", ["Yellow"] = "#F4D03F", ["Mustard"] = "#D4A017",
        ["Orange"] = "#F28C28", ["Rust"] = "#B7410E", ["Red"] = "#C62828", ["Burgundy"] = "#800020",
        ["Maroon"] = "#5A1A1A", ["Pink"] = "#F4A6C0", ["Hot Pink"] = "#E91E8C", ["Purple"] = "#6A3FA0",
        ["Lilac"] = "#C8A2C8", ["Brown"] = "#6B4226", ["Tan"] = "#C19A6B", ["Beige"] = "#E8DCC4",
        ["Cream"] = "#FFF8E1", ["Gold"] = "#D4AF37", ["Multicolour"] = "#999999"
    };

    /// <summary>Colour is a closed list picked from a real dropdown per variant row. Edit ColorHexMap to change the set.</summary>
    public static readonly string[] Colors = ColorHexMap.Keys.ToArray();

    /// <summary>No fixed seed for Brand - the dropdown is built entirely from brands already used on existing products.</summary>
    public static readonly string[] Brands = Array.Empty<string>();
}

/// <summary>One size/colour row on the Create/Edit Product form. ProductVariantId is 0 for a
/// brand-new row that hasn't been saved yet.</summary>
public class ProductVariantInputViewModel
{
    public int ProductVariantId { get; set; }

    [MaxLength(20)]
    [RegularExpression(ProductViewModel.OptionalSizePattern, ErrorMessage = "Size looks invalid - try something like S, M, L, 7, 9.5, or UK 7.")]
    public string? Size { get; set; }

    [MaxLength(30)]
    [RegularExpression(ProductViewModel.OptionalAttributeWordPattern, ErrorMessage = "Colour can only contain letters, spaces and hyphens - no numbers or symbols.")]
    public string? Color { get; set; }

    /// <summary>Hex value for the storefront swatch dot, e.g. "#000000". Optional - falls back to a neutral grey dot if omitted.</summary>
    [MaxLength(10)]
    public string? ColorHex { get; set; }

    [Range(0, 10000, ErrorMessage = "Stock quantity must be between 0 and 10,000.")]
    [Display(Name = "Stock Quantity")]
    public int StockQuantity { get; set; }

    /// <summary>Read-only display once assigned; null while the row is still unsaved on Create.</summary>
    public string? SKU { get; set; }

    /// <summary>Only set when this specific size/colour costs more or less than the style's base SellingPrice.</summary>
    [Range(0, 100000)]
    public decimal? PriceOverride { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>UI-only flag so the Edit form can mark a row for deletion before it's ever been sold (a variant with sales history is deactivated instead, never deleted).</summary>
    public bool Remove { get; set; }
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

/// <summary>Search/filter criteria for the master catalogue view. Size/Colour now filter on
/// "has at least one matching active variant" rather than a direct column equality.</summary>
public class ProductFilterViewModel
{
    public string? SearchTerm { get; set; }
    public string? Category { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public bool? InStockOnly { get; set; }
}
