using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum RefundMethod
{
    OriginalPayment,
    StoreCredit
}

public class ReturnTransaction
{
    [Key]
    public int ReturnId { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public string ProcessedByUserId { get; set; } = string.Empty;
    public ApplicationUser? ProcessedByUser { get; set; }

    public int QuantityReturned { get; set; }
    public bool IsResalable { get; set; } = true; // whether item is restocked

    public RefundMethod RefundMethod { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundAmount { get; set; }

    [MaxLength(250)]
    public string? Reason { get; set; }

    public DateTime DateProcessed { get; set; } = DateTime.UtcNow;
}

public enum InventoryChangeReason
{
    Sale,
    Return,
    PurchaseOrderReceived,
    ManualAdjustment
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
