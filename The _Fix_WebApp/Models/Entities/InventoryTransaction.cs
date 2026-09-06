using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

public enum InventoryChangeReason
{
    Sale,
    Return,
    PurchaseOrderReceived,
    ManualAdjustment,
    OrderCancelled
}

/// <summary>Audit trail of every stock quantity change, for traceability. Stock now lives on
/// ProductVariant, so every transaction records which variant (size/colour) moved; ProductId
/// stays denormalized alongside it purely so style-level reporting never needs to join
/// through ProductVariant.</summary>
public class InventoryTransaction
{
    [Key]
    public int InventoryTransactionId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    /// <summary>Positive = stock added, Negative = stock removed.</summary>
    public int QuantityChange { get; set; }

    public InventoryChangeReason Reason { get; set; }

    public DateTime DateRecorded { get; set; } = DateTime.UtcNow;
}
