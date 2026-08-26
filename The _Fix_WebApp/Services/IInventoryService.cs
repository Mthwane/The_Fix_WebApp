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
}
