using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum RefundMethod
{
    OriginalPayment,
    StoreCredit
}

/// <summary>
/// A processed return against a line on a past order. Ported from V1 with one substantive change:
/// restocking now targets the specific ProductVariant that came back, not the parent style, so a
/// returned "M/Black" goes back into M/Black stock rather than being smeared across the style.
/// </summary>
public class ReturnTransaction
{
    [Key]
    public int ReturnId { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    /// <summary>The exact size/colour returned. Nullable because a historical order line raised
    /// before variants existed has no variant to point at - those restock manually.</summary>
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public string ProcessedByUserId { get; set; } = string.Empty;
    public ApplicationUser? ProcessedByUser { get; set; }

    public int QuantityReturned { get; set; }

    /// <summary>Whether the item goes back on the shelf. False (damaged/worn) means the customer
    /// is still refunded but stock is NOT incremented.</summary>
    public bool IsResalable { get; set; } = true;

    public RefundMethod RefundMethod { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundAmount { get; set; }

    [MaxLength(250)]
    public string? Reason { get; set; }

    public DateTime DateProcessed { get; set; } = DateTime.UtcNow;
}
