using System.ComponentModel.DataAnnotations;
using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>Backs the Create/Edit discount form.</summary>
public class DiscountFormViewModel : IValidatableObject
{
    public int? DiscountId { get; set; }

    [Display(Name = "Code")]
    [MaxLength(30)]
    [RegularExpression(@"^[A-Za-z0-9\-]{3,30}$", ErrorMessage = "Use 3-30 letters, numbers or dashes (no spaces).")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Give the discount a name so staff can recognise it.")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public DiscountType Type { get; set; } = DiscountType.Percentage;

    [Required(ErrorMessage = "Enter the discount value.")]
    [Range(0.01, 1000000, ErrorMessage = "The value must be greater than zero.")]
    public decimal? Value { get; set; }

    [Display(Name = "Max discount per order (R)")]
    [Range(0.01, 1000000, ErrorMessage = "The cap must be greater than zero.")]
    public decimal? MaxDiscountAmount { get; set; }

    [Display(Name = "Minimum spend (R)")]
    [Range(0.01, 1000000, ErrorMessage = "The minimum spend must be greater than zero.")]
    public decimal? MinimumSpend { get; set; }

    [Display(Name = "Total uses allowed")]
    [Range(1, 10000000, ErrorMessage = "Enter at least 1, or leave blank for unlimited.")]
    public int? MaxRedemptions { get; set; }

    [Display(Name = "Uses per customer")]
    [Range(1, 10000, ErrorMessage = "Enter at least 1, or leave blank for unlimited.")]
    public int? MaxUsesPerCustomer { get; set; }

    public DiscountChannel Channel { get; set; } = DiscountChannel.Both;

    /// <summary>Local date the discount starts (blank = right now).</summary>
    [DataType(DataType.Date)]
    public DateTime? StartsOn { get; set; }

    [Display(Name = "Valid for (days)")]
    [Range(1, 3650, ErrorMessage = "Enter between 1 and 3650 days, or leave blank.")]
    public int? ValidForDays { get; set; }

    /// <summary>Local date the discount last works (used only when "valid for days" is blank).</summary>
    [DataType(DataType.Date)]
    public DateTime? ExpiresOn { get; set; }

    public bool IsActive { get; set; } = true;

    public bool ShowBanner { get; set; }

    [MaxLength(200)]
    public string? BannerText { get; set; }

    // --- Linking to the catalogue ---
    public List<int> ProductIds { get; set; } = new();
    public List<int> SupplierIds { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<string> Brands { get; set; } = new();

    // --- Create only ---
    /// <summary>Make this many unique single-use codes from this template (1 = just the one code above).</summary>
    [Range(1, 200, ErrorMessage = "Generate between 1 and 200 codes at a time.")]
    public int BulkCount { get; set; } = 1;

    [MaxLength(10)]
    [RegularExpression(@"^[A-Za-z0-9]*$", ErrorMessage = "The prefix can only contain letters and numbers.")]
    public string? CodePrefix { get; set; }

    /// <summary>On edit: the current redemption count (the code can't be renamed once it has been used).</summary>
    public int RedemptionCount { get; set; }

    // --- Pick lists for the form ---
    public List<ProductChoice> ProductChoices { get; set; } = new();
    public List<SupplierChoice> SupplierChoices { get; set; } = new();
    public List<string> CategoryChoices { get; set; } = new();
    public List<string> BrandChoices { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == DiscountType.Percentage && Value is > 100)
            yield return new ValidationResult("A percentage discount can't be more than 100%.", new[] { nameof(Value) });

        if (!ValidForDays.HasValue && ExpiresOn.HasValue && StartsOn.HasValue && ExpiresOn.Value.Date < StartsOn.Value.Date)
            yield return new ValidationResult("The expiry date can't be before the start date.", new[] { nameof(ExpiresOn) });

        if (ShowBanner && Channel == DiscountChannel.InStore)
            yield return new ValidationResult("An in-store-only discount can't have a storefront banner. Change the channel to Online or Both.", new[] { nameof(ShowBanner) });
    }
}

public class ProductChoice
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
}

public class SupplierChoice
{
    public int SupplierId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>One discount row in the staff list.</summary>
public class DiscountListItemViewModel
{
    public Discount Discount { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public string AppliesTo { get; set; } = string.Empty;
}

public class DiscountDetailsViewModel
{
    public Discount Discount { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public List<string> TargetLabels { get; set; } = new();
    public List<DiscountRedemption> RecentRedemptions { get; set; } = new();
    public Dictionary<int, string> OrderNumbers { get; set; } = new();
    public decimal TotalDiscounted { get; set; }
}
