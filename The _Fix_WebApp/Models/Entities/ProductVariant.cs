using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// A single purchasable Size/Colour combination of a Product ("style"). This is what actually
/// carries a scannable SKU and a stock count - Product is now just the shared style shell
/// (name, description, brand, base price, department, etc.). Every place that used to
/// scan/cart/decrement against Product.SKU or Product.StockQuantity now does so against a
/// ProductVariant instead (see IInventoryService and PosController/ShopController).
///
/// Matches the Figma storefront designs, which show several colour swatches and a size range
/// per product card - each dot/size is a distinct ProductVariant with its own stock, not a
/// single Size/Colour string on the product itself.
/// </summary>
public class ProductVariant
{
    [Key]
    public int ProductVariantId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>
    /// The scannable/sellable SKU, unique across the whole catalogue - e.g. "CLO-0001-M-BLK"
    /// (style code + size + colour code). Generated server-side, same rule as the old
    /// Product.SKU generation just extended with a variant suffix (see
    /// ProductsController.GenerateUniqueVariantSkuAsync).
    /// </summary>
    [Required, MaxLength(60)]
    public string SKU { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Size { get; set; }

    [MaxLength(30)]
    public string? Color { get; set; }

    /// <summary>Hex value for rendering a colour swatch dot on the storefront - e.g. "#000000".</summary>
    [MaxLength(10)]
    public string? ColorHex { get; set; }

    /// <summary>
    /// Null = use Product.SellingPrice as-is. Set only when this specific size/colour costs
    /// more or less than the style's base price (e.g. a plus-size surcharge, or a premium
    /// colourway) - mirrors how CompareAtPrice works on Product for sale pricing.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? PriceOverride { get; set; }

    public int StockQuantity { get; set; }

    /// <summary>Soft-delete flag - a discontinued size/colour is deactivated, never deleted, consistent with Product.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime? DateUpdated { get; set; }

    // --- Navigation ---
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    /// <summary>The price actually charged for this variant - falls back to the parent style's price when there's no override.</summary>
    [NotMapped]
    public decimal EffectivePrice => PriceOverride ?? Product?.SellingPrice ?? 0;

    /// <summary>Low stock is now judged per-variant against the parent style's configured threshold - a product can be low on "M/Black" while "L/Black" is fine.</summary>
    [NotMapped]
    public bool IsLowStock => Product is not null && StockQuantity <= Product.LowStockThreshold;
}
