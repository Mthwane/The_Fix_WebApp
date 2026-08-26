using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

/// <summary>
/// Central place for all stock-count changes so every code path (POS sale,
/// PO receipt, return, manual adjustment) goes through the same auditing logic.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(ApplicationDbContext context, ILogger<InventoryService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task DecrementStockAsync(int productId, int quantity, InventoryChangeReason reason = InventoryChangeReason.Sale)
    {
        var product = await _context.Products.FindAsync(productId)
            ?? throw new InvalidOperationException($"Product {productId} not found.");

        product.StockQuantity -= quantity;
        product.DateUpdated = DateTime.UtcNow;

        _context.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = productId,
            QuantityChange = -quantity,
            Reason = reason
        });

        await _context.SaveChangesAsync();

        if (product.IsLowStock)
        {
            // TODO: hook into a notification pipeline (email/SignalR) for management alerts.
            _logger.LogWarning("Low stock alert: {ProductName} ({SKU}) is at {Stock} units.",
                product.Name, product.SKU, product.StockQuantity);
        }
    }

    public async Task IncrementStockAsync(int productId, int quantity, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived)
    {
        var product = await _context.Products.FindAsync(productId)
            ?? throw new InvalidOperationException($"Product {productId} not found.");

        product.StockQuantity += quantity;
        product.DateUpdated = DateTime.UtcNow;

        _context.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = productId,
            QuantityChange = quantity,
            Reason = reason
        });

        await _context.SaveChangesAsync();
    }

    public async Task<List<Product>> GetLowStockProductsAsync()
    {
        return await _context.Products
            .Where(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold)
            .OrderBy(p => p.StockQuantity)
            .ToListAsync();
    }

    public async Task<bool> IsLowStockAsync(int productId)
    {
        var product = await _context.Products.FindAsync(productId);
        return product is not null && product.IsLowStock;
    }
}
