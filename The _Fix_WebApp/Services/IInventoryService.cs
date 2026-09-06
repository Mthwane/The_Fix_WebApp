using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services;

public interface IInventoryService
{
    /// <summary>Decrements stock for a sold variant (a specific size/colour) and logs the movement. Called from POS checkout / order fulfillment.</summary>
    Task DecrementStockAsync(int variantId, int quantity, InventoryChangeReason reason = InventoryChangeReason.Sale);

    /// <summary>Increments stock for a variant, e.g. when a supplier shipment or a return is received.</summary>
    Task IncrementStockAsync(int variantId, int quantity, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived);

    /// <summary>Returns all active variants whose stock has fallen at or below their style's threshold (each row includes its parent Product).</summary>
    Task<List<ProductVariant>> GetLowStockVariantsAsync();

    /// <summary>True if a specific variant's stock is at or below its style's configured threshold.</summary>
    Task<bool> IsLowStockAsync(int variantId);

    /// <summary>
    /// Decrements stock for every (variantId, quantity) line in a single round trip and a
    /// single SaveChanges - use this instead of looping DecrementStockAsync per line (e.g.
    /// POS checkout or online order fulfillment), which otherwise does one query + one
    /// commit per cart line. Returns the updated variants so callers can check IsLowStock
    /// without re-querying.
    /// </summary>
    Task<List<ProductVariant>> DecrementStockBatchAsync(IEnumerable<(int VariantId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.Sale);

    /// <summary>Batch equivalent of IncrementStockAsync - one round trip for the whole order/return.</summary>
    Task<List<ProductVariant>> IncrementStockBatchAsync(IEnumerable<(int VariantId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived);
}
