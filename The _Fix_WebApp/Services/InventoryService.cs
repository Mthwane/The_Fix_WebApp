using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq;

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
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Decrement quantity must be positive.");
        if (product.StockQuantity < quantity)
            throw new InvalidOperationException($"Cannot decrement stock for '{product.Name}' below zero (have {product.StockQuantity}, need {quantity}).");


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

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Increment quantity must be positive.");
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

    public async Task<List<Product>> DecrementStockBatchAsync(IEnumerable<(int ProductId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.Sale)
    {
        var linesList = lines.ToList();
        if (linesList.Count == 0) return new List<Product>();

        // One query for every product in the cart instead of one query per line.
        var productIds = linesList.Select(l => l.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId);

        foreach (var (productId, quantity) in linesList)
        {
            if (!products.TryGetValue(productId, out var product))
                throw new InvalidOperationException($"Product {productId} not found.");
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Decrement quantity must be positive.");
            if (product.StockQuantity < quantity)
                throw new InvalidOperationException($"Cannot decrement stock for '{product.Name}' below zero (have {product.StockQuantity}, need {quantity}).");

            product.StockQuantity -= quantity;
            product.DateUpdated = DateTime.UtcNow;

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = productId,
                QuantityChange = -quantity,
                Reason = reason
            });
        }

        // One commit for the whole basket instead of one commit per line.
        await _context.SaveChangesAsync();

        foreach (var product in products.Values.Where(p => p.IsLowStock))
        {
            // TODO: hook into a notification pipeline (email/SignalR) for management alerts.
            _logger.LogWarning("Low stock alert: {ProductName} ({SKU}) is at {Stock} units.",
                product.Name, product.SKU, product.StockQuantity);
        }

        return products.Values.ToList();
    }

    public async Task<List<Product>> IncrementStockBatchAsync(IEnumerable<(int ProductId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived)
    {
        var linesList = lines.ToList();
        if (linesList.Count == 0) return new List<Product>();

        var productIds = linesList.Select(l => l.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId);

        foreach (var (productId, quantity) in linesList)
        {
            if (!products.TryGetValue(productId, out var product))
                throw new InvalidOperationException($"Product {productId} not found.");
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Increment quantity must be positive.");

            product.StockQuantity += quantity;
            product.DateUpdated = DateTime.UtcNow;

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = productId,
                QuantityChange = quantity,
                Reason = reason
            });
        }

        await _context.SaveChangesAsync();

        return products.Values.ToList();
    }
}
