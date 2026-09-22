using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

/// <summary>
/// Central place for all stock-count changes so every code path (POS sale,
/// PO receipt, return, manual adjustment) goes through the same auditing logic.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        ILogger<InventoryService> logger)
    {
        _context = context;
        _userManager = userManager;
        _emailSender = emailSender;
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

        var wasLowStock = product.IsLowStock;

        product.StockQuantity -= quantity;
        product.DateUpdated = DateTime.UtcNow;

        _context.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = productId,
            QuantityChange = -quantity,
            Reason = reason
        });

        await _context.SaveChangesAsync();

        // Only notify the moment stock CROSSES INTO low-stock territory, not on every
        // sale after it's already low - otherwise managers get spammed with one email
        // per sale of an already-known-low item.
        if (product.IsLowStock && !wasLowStock)
            await NotifyManagersOfLowStockAsync(new List<Product> { product });
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

        // Snapshot "already low" BEFORE mutating, so we can tell who just crossed the line.
        var wasLowStockIds = products.Values.Where(p => p.IsLowStock).Select(p => p.ProductId).ToHashSet();

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

        var newlyLowStock = products.Values.Where(p => p.IsLowStock && !wasLowStockIds.Contains(p.ProductId)).ToList();
        if (newlyLowStock.Count > 0)
            await NotifyManagersOfLowStockAsync(newlyLowStock);

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

    /// <summary>
    /// Emails everyone in the "Manager" role - and only that role, by design - whenever one
    /// or more products just crossed into low-stock territory. Deliberately narrower than
    /// "every staff member with product-management access" (which would also include
    /// Administrators): if you want Administrators/Owners notified too, add their role names
    /// to the array below.
    /// </summary>
    private async Task NotifyManagersOfLowStockAsync(List<Product> products)
    {
        if (products.Count == 0) return;

        try
        {
            var managers = await _userManager.GetUsersInRoleAsync("Manager");
            var recipients = managers.Where(m => m.IsActive && !string.IsNullOrWhiteSpace(m.Email)).ToList();

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "{Count} product(s) just went low on stock, but no active Manager has an email address to notify.",
                    products.Count);
                return;
            }

            var rows = string.Join("", products.Select(p =>
                $"<tr><td>{p.Name}</td><td>{p.SKU}</td><td>{p.StockQuantity}</td><td>{p.LowStockThreshold}</td></tr>"));

            var body = $@"
                <h2>Low stock alert</h2>
                <p>{products.Count} product(s) just dropped to or below their restock threshold:</p>
                <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                    <thead><tr><th>Product</th><th>SKU</th><th>Current Stock</th><th>Threshold</th></tr></thead>
                    <tbody>{rows}</tbody>
                </table>
                <p>Log in to the dashboard's Low Stock page to review and restock.</p>";

            var subject = products.Count == 1
                ? $"Low stock alert - {products[0].Name}"
                : $"Low stock alert - {products.Count} items need restocking";

            foreach (var manager in recipients)
                await _emailSender.SendAsync(manager.Email!, subject, body);
        }
        catch (Exception ex)
        {
            // Never let a notification failure break the caller (a checkout, a sale, etc).
            _logger.LogError(ex, "Failed to send low-stock notification email.");
        }
    }
}