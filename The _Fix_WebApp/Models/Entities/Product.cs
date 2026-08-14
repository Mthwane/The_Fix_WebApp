using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public class Product
{
    [Key]
    public int ProductId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string SKU { get; set; } = string.Empty; // Barcode / SKU, unique

    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty; // Clothing, Shoes, Accessories...

    [MaxLength(20)]
    public string? Size { get; set; }

    [MaxLength(30)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string? Brand { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SellingPrice { get; set; }

    public string? ImageUrl { get; set; }

    public int StockQuantity { get; set; }

    /// <summary>When StockQuantity falls below this, the item is flagged low-stock.</summary>
    public int LowStockThreshold { get; set; } = 5;

    /// <summary>Soft-delete flag - phased-out products are deactivated, not deleted.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime? DateUpdated { get; set; }

    // --- Navigation ---
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    [NotMapped]
    public bool IsLowStock => StockQuantity <= LowStockThreshold;
}
