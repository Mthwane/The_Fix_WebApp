using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum DiscountType
{
    /// <summary>Value is a percentage (0-100) of the eligible subtotal.</summary>
    Percentage = 0,
    /// <summary>Value is a fixed rand amount off the eligible subtotal.</summary>
    FixedAmount = 1
}

/// <summary>Where a discount may be redeemed.</summary>
public enum DiscountChannel
{
    Both = 0,
    Online = 1,
    InStore = 2
}

/// <summary>What a discount is linked to. A discount with NO targets applies to the whole basket.</summary>
public enum DiscountTargetType
{
    Product = 0,
    Category = 1,
    Brand = 2,
    Supplier = 3
}

/// <summary>
/// A redeemable discount code. Staff create them (typed in by hand or generated) and customise every
/// limit; customers enter the code at online checkout and cashiers enter it at the till, which replaces the old
/// free-typed "discount amount" box so every discount is traceable to a real, rule-checked code.
///
/// Every rule below is enforced SERVER-SIDE in IDiscountService - the browser only ever previews.
/// </summary>
public class Discount
{
    [Key]
    public int DiscountId { get; set; }

    /// <summary>What the customer/cashier types. Always stored upper-case, unique.</summary>
    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Internal label shown to staff, e.g. "Summer Sale 2026".</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public DiscountType Type { get; set; } = DiscountType.Percentage;

    /// <summary>Percentage (0-100) or rand amount, depending on Type.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Value { get; set; }

    /// <summary>Hard ceiling on how much ONE order can save with this code (null = no ceiling).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>Minimum eligible spend before the code works (null = none).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? MinimumSpend { get; set; }

    /// <summary>Total number of times the code may be used across everyone (null = unlimited).</summary>
    public int? MaxRedemptions { get; set; }

    /// <summary>How many times one signed-in customer may use it (null = unlimited).</summary>
    public int? MaxUsesPerCustomer { get; set; }

    public int RedemptionCount { get; set; }

    public DiscountChannel Channel { get; set; } = DiscountChannel.Both;

    public DateTime StartsAt { get; set; } = DateTime.UtcNow;

    /// <summary>"Valid for N days" as entered by staff (informational - ExpiresAt is what's enforced).</summary>
    public int? ValidForDays { get; set; }

    /// <summary>Last moment the code works (UTC). Null = never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Master switch - an inactive code can't be redeemed anywhere, whatever its dates say.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Show a promotional banner on the storefront while this discount is live.</summary>
    public bool ShowBanner { get; set; }

    [MaxLength(200)]
    public string? BannerText { get; set; }

    /// <summary>True when the code was produced by the generator rather than typed in.</summary>
    public bool IsGenerated { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public ICollection<DiscountTarget> Targets { get; set; } = new List<DiscountTarget>();
    public ICollection<DiscountRedemption> Redemptions { get; set; } = new List<DiscountRedemption>();

    /// <summary>Whether the code can be redeemed right now, ignoring basket-specific rules.</summary>
    [NotMapped]
    public bool IsLive => IsLiveAt(DateTime.UtcNow);

    public bool IsLiveAt(DateTime utcNow) =>
        IsActive
        && StartsAt <= utcNow
        && (!ExpiresAt.HasValue || ExpiresAt.Value >= utcNow)
        && (!MaxRedemptions.HasValue || RedemptionCount < MaxRedemptions.Value);

    /// <summary>One word for the staff list: Active / Scheduled / Expired / Used up / Disabled.</summary>
    public string StatusLabel(DateTime utcNow)
    {
        if (!IsActive) return "Disabled";
        if (StartsAt > utcNow) return "Scheduled";
        if (ExpiresAt.HasValue && ExpiresAt.Value < utcNow) return "Expired";
        if (MaxRedemptions.HasValue && RedemptionCount >= MaxRedemptions.Value) return "Used up";
        return "Active";
    }

    /// <summary>Short human description, e.g. "15% off" or "R50 off".</summary>
    public string ValueLabel() =>
        Type == DiscountType.Percentage
            ? $"{Value:0.##}% off"
            : $"R{Value:0.##} off";
}

/// <summary>
/// Links a discount to part of the catalogue. With one or more targets the discount only applies to basket lines
/// matching ANY target (a specific product, a whole category, a brand, or everything from one supplier).
/// ProductId / SupplierId are plain ids (no foreign key) because products are soft-deleted and a stale link
/// simply stops matching.
/// </summary>
public class DiscountTarget
{
    [Key]
    public int DiscountTargetId { get; set; }

    public int DiscountId { get; set; }
    public Discount Discount { get; set; } = null!;

    public DiscountTargetType Type { get; set; }

    public int? ProductId { get; set; }
    public int? SupplierId { get; set; }

    /// <summary>Category or brand name, when Type is Category / Brand.</summary>
    [MaxLength(100)]
    public string? Value { get; set; }
}

/// <summary>One use of a discount on one order - the audit trail behind RedemptionCount and the per-customer limit.</summary>
public class DiscountRedemption
{
    [Key]
    public int DiscountRedemptionId { get; set; }

    public int DiscountId { get; set; }
    public Discount Discount { get; set; } = null!;

    public int OrderId { get; set; }

    [MaxLength(450)]
    public string? CustomerId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountDiscounted { get; set; }

    public DateTime DateRedeemed { get; set; } = DateTime.UtcNow;
}
