using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services;

public interface IInventoryService
{
    /// <summary>Decrements stock for a sold item and logs the movement. Called from POS checkout.</summary>
    Task DecrementStockAsync(int productId, int quantity, InventoryChangeReason reason = InventoryChangeReason.Sale);

    /// <summary>Increments stock, e.g. when a supplier shipment or a return is received.</summary>
    Task IncrementStockAsync(int productId, int quantity, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived);

    /// <summary>Returns all active products whose stock has fallen at or below their threshold.</summary>
    Task<List<Product>> GetLowStockProductsAsync();

    /// <summary>True if a product's stock is at or below its configured threshold.</summary>
    Task<bool> IsLowStockAsync(int productId);
<<<<<<< HEAD

    /// <summary>
    /// Decrements stock for every (productId, quantity) line in a single round trip and a
    /// single SaveChanges - use this instead of looping DecrementStockAsync per line (e.g.
    /// POS checkout), which otherwise does one query + one commit per cart line.
    /// Returns the updated products so callers can check IsLowStock without re-querying.
    /// </summary>
    Task<List<Product>> DecrementStockBatchAsync(IEnumerable<(int ProductId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.Sale);

    /// <summary>Batch equivalent of IncrementStockAsync - one round trip for the whole order.</summary>
    Task<List<Product>> IncrementStockBatchAsync(IEnumerable<(int ProductId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived);
=======
>>>>>>> origin/SprintPresent
}
