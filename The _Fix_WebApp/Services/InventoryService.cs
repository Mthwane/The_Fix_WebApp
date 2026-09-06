using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

/// <summary>
/// Central place for all stock-count changes so every code path (POS sale, online order,
/// PO receipt, return, manual adjustment) goes through the same auditing logic. Stock lives
/// on ProductVariant (one row per size/colour) rather than on Product itself, so every
/// method here takes a variantId, not a productId.
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

    public async Task DecrementStockAsync(int variantId, int quantity, InventoryChangeReason reason = InventoryChangeReason.Sale)
    {
        var variant = await _context.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.ProductVariantId == variantId)
            ?? throw new InvalidOperationException($"Product variant {variantId} not found.");
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Decrement quantity must be positive.");
        if (variant.StockQuantity < quantity)
            throw new InvalidOperationException($"Cannot decrement stock for '{variant.Product.Name} ({variant.Size}/{variant.Color})' below zero (have {variant.StockQuantity}, need {quantity}).");

        var wasLowStock = variant.IsLowStock;

        variant.StockQuantity -= quantity;
        variant.DateUpdated = DateTime.UtcNow;

        _context.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = variant.ProductId,
            ProductVariantId = variantId,
            QuantityChange = -quantity,
            Reason = reason
        });

        await _context.SaveChangesAsync();

        // Only notify the moment stock CROSSES INTO low-stock territory, not on every
        // sale after it's already low - otherwise managers get spammed with one email
        // per sale of an already-known-low item.
        if (variant.IsLowStock && !wasLowStock)
            await NotifyManagersOfLowStockAsync(new List<ProductVariant> { variant });
    }

    public async Task IncrementStockAsync(int variantId, int quantity, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived)
    {
        var variant = await _context.ProductVariants.FindAsync(variantId)
            ?? throw new InvalidOperationException($"Product variant {variantId} not found.");

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Increment quantity must be positive.");
        variant.StockQuantity += quantity;
        variant.DateUpdated = DateTime.UtcNow;

        _context.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = variant.ProductId,
            ProductVariantId = variantId,
            QuantityChange = quantity,
            Reason = reason
        });

        await _context.SaveChangesAsync();
    }

    public async Task<List<ProductVariant>> GetLowStockVariantsAsync()
    {
        return await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product.IsActive && v.StockQuantity <= v.Product.LowStockThreshold)
            .OrderBy(v => v.StockQuantity)
            .ToListAsync();
    }

    public async Task<bool> IsLowStockAsync(int variantId)
    {
        var variant = await _context.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.ProductVariantId == variantId);
        return variant is not null && variant.IsLowStock;
    }

    public async Task<List<ProductVariant>> DecrementStockBatchAsync(IEnumerable<(int VariantId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.Sale)
    {
        var linesList = lines.ToList();
        if (linesList.Count == 0) return new List<ProductVariant>();

        // One query for every variant in the cart instead of one query per line.
        var variantIds = linesList.Select(l => l.VariantId).Distinct().ToList();
        var variants = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => variantIds.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        // Snapshot "already low" BEFORE mutating, so we can tell who just crossed the line.
        var wasLowStockIds = variants.Values.Where(v => v.IsLowStock).Select(v => v.ProductVariantId).ToHashSet();

        foreach (var (variantId, quantity) in linesList)
        {
            if (!variants.TryGetValue(variantId, out var variant))
                throw new InvalidOperationException($"Product variant {variantId} not found.");
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Decrement quantity must be positive.");
            if (variant.StockQuantity < quantity)
                throw new InvalidOperationException($"Cannot decrement stock for '{variant.Product.Name} ({variant.Size}/{variant.Color})' below zero (have {variant.StockQuantity}, need {quantity}).");

            variant.StockQuantity -= quantity;
            variant.DateUpdated = DateTime.UtcNow;

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = variant.ProductId,
                ProductVariantId = variantId,
                QuantityChange = -quantity,
                Reason = reason
            });
        }

        // One commit for the whole basket instead of one commit per line.
        await _context.SaveChangesAsync();

        var newlyLowStock = variants.Values.Where(v => v.IsLowStock && !wasLowStockIds.Contains(v.ProductVariantId)).ToList();
        if (newlyLowStock.Count > 0)
            await NotifyManagersOfLowStockAsync(newlyLowStock);

        return variants.Values.ToList();
    }

    public async Task<List<ProductVariant>> IncrementStockBatchAsync(IEnumerable<(int VariantId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived)
    {
        var linesList = lines.ToList();
        if (linesList.Count == 0) return new List<ProductVariant>();

        var variantIds = linesList.Select(l => l.VariantId).Distinct().ToList();
        var variants = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => variantIds.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        foreach (var (variantId, quantity) in linesList)
        {
            if (!variants.TryGetValue(variantId, out var variant))
                throw new InvalidOperationException($"Product variant {variantId} not found.");
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Increment quantity must be positive.");

            variant.StockQuantity += quantity;
            variant.DateUpdated = DateTime.UtcNow;

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = variant.ProductId,
                ProductVariantId = variantId,
                QuantityChange = quantity,
                Reason = reason
            });
        }

        await _context.SaveChangesAsync();

        return variants.Values.ToList();
    }

    /// <summary>
    /// Emails everyone in the "Manager" role - and only that role, by design - whenever one
    /// or more variants just crossed into low-stock territory. Deliberately narrower than
    /// "every staff member with product-management access" (which would also include
    /// Administrators): if you want Administrators/Owners notified too, add their role names
    /// to the array below.
    /// </summary>
    private async Task NotifyManagersOfLowStockAsync(List<ProductVariant> variants)
    {
        if (variants.Count == 0) return;

        try
        {
            var managers = await _userManager.GetUsersInRoleAsync("Manager");
            var recipients = managers.Where(m => m.IsActive && !string.IsNullOrWhiteSpace(m.Email)).ToList();

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "{Count} variant(s) just went low on stock, but no active Manager has an email address to notify.",
                    variants.Count);
                return;
            }

            var rows = string.Join("", variants.Select(v =>
                $"<tr><td>{v.Product.Name}</td><td>{v.Size}/{v.Color}</td><td>{v.SKU}</td><td>{v.StockQuantity}</td><td>{v.Product.LowStockThreshold}</td></tr>"));

            var body = $@"
                <h2>Low stock alert</h2>
                <p>{variants.Count} variant(s) just dropped to or below their restock threshold:</p>
                <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                    <thead><tr><th>Product</th><th>Size/Colour</th><th>SKU</th><th>Current Stock</th><th>Threshold</th></tr></thead>
                    <tbody>{rows}</tbody>
                </table>
                <p>Log in to the dashboard's Low Stock page to review and restock.</p>";

            var subject = variants.Count == 1
                ? $"Low stock alert - {variants[0].Product.Name} ({variants[0].Size}/{variants[0].Color})"
                : $"Low stock alert - {variants.Count} variants need restocking";

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
