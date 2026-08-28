using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum OrderType
{
    POS,     // In-store till transaction
    Online   // Customer web checkout
}

public enum OrderStatus
{
    Pending,
    Processing,
    Completed,
    Shipped,
    Delivered,
    Cancelled,
    Returned
}

public enum PaymentMethod
{
    Cash,
    CreditCard,
    DebitCard
}

public class Order
{
    [Key]
    public int OrderId { get; set; }

    [Required, MaxLength(30)]
    public string OrderNumber { get; set; } = string.Empty; // human-readable receipt / order ref

    // Customer who placed / owns the order (nullable for walk-in cash sales with no account)
    public string? CustomerId { get; set; }
    public ApplicationUser? Customer { get; set; }

    // Employee who processed the sale at the till
    public string? ProcessedByUserId { get; set; }
    public ApplicationUser? ProcessedByUser { get; set; }

    public OrderType OrderType { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal GrandTotal { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? DateFulfilled { get; set; }

    // --- Navigation ---
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    [Key]
    public int OrderItemId { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; } // price at time of sale

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }
}
