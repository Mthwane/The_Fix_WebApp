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

/// <summary>Audit trail of every stock quantity change, for traceability.</summary>
public class InventoryTransaction
{
    [Key]
    public int InventoryTransactionId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Positive = stock added, Negative = stock removed.</summary>
    public int QuantityChange { get; set; }

    public InventoryChangeReason Reason { get; set; }

    public DateTime DateRecorded { get; set; } = DateTime.UtcNow;
}
