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
///
/// CONCURRENCY: stock is never changed by "read the number, add/subtract in memory, write the new
/// number back" - that lets two simultaneous sales each read 1 and both succeed. Every change is ONE
/// atomic UPDATE (StockQuantity = StockQuantity -/+ n), and a decrement is conditional
/// (WHERE StockQuantity >= n), so the database itself refuses to oversell however many requests land
/// at the same moment.
///
/// TRANSACTIONS: a multi-line change runs in one transaction so a basket is applied fully or not at
/// all. If the caller already opened a transaction (POS checkout and online order creation do, so the
/// order row and its stock movement commit or roll back together) this joins it and leaves
/// commit/rollback to the caller.
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
        => await DecrementStockBatchAsync(new[] { (variantId, quantity) }, reason);

    public async Task IncrementStockAsync(int variantId, int quantity, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived)
        => await IncrementStockBatchAsync(new[] { (variantId, quantity) }, reason);

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
        var variant = await _context.ProductVariants.AsNoTracking().Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.ProductVariantId == variantId);
        return variant is not null && variant.IsLowStock;
    }

    public async Task<List<ProductVariant>> DecrementStockBatchAsync(IEnumerable<(int VariantId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.Sale)
    {
        var grouped = Aggregate(lines);
        if (grouped.Count == 0) return new List<ProductVariant>();

        var ids = grouped.Keys.ToList();
        var before = await LoadFreshAsync(ids);
        var wasLowStockIds = before.Where(v => v.IsLowStock).Select(v => v.ProductVariantId).ToHashSet();
        var productIds = before.ToDictionary(v => v.ProductVariantId, v => v.ProductId);

        await InTransactionAsync(async () =>
        {
            var now = DateTime.UtcNow;

            // Ordered by id so two baskets touching the same variants always take their locks in the same order.
            foreach (var (variantId, quantity) in grouped.OrderBy(g => g.Key))
            {
                if (!productIds.TryGetValue(variantId, out var productId))
                    throw new InvalidOperationException($"Product variant {variantId} not found.");

                var affected = await _context.ProductVariants
                    .Where(v => v.ProductVariantId == variantId && v.StockQuantity >= quantity)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.StockQuantity, v => v.StockQuantity - quantity)
                        .SetProperty(v => v.DateUpdated, v => (DateTime?)now));

                if (affected == 0)
                    throw await DescribeFailureAsync(variantId, quantity);

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = productId,
                    ProductVariantId = variantId,
                    QuantityChange = -quantity,
                    Reason = reason
                });
            }

            await _context.SaveChangesAsync();
        });

        var after = await LoadFreshAsync(ids);

        // Only notify the moment stock CROSSES INTO low-stock territory, not on every sale after
        // it's already low - otherwise managers get one email per sale of an already-known-low item.
        var newlyLowStock = after.Where(v => v.IsLowStock && !wasLowStockIds.Contains(v.ProductVariantId)).ToList();
        if (newlyLowStock.Count > 0)
            await NotifyManagersOfLowStockAsync(newlyLowStock);

        return after;
    }

    public async Task<List<ProductVariant>> IncrementStockBatchAsync(IEnumerable<(int VariantId, int Quantity)> lines, InventoryChangeReason reason = InventoryChangeReason.PurchaseOrderReceived)
    {
        var grouped = Aggregate(lines);
        if (grouped.Count == 0) return new List<ProductVariant>();

        var ids = grouped.Keys.ToList();
        var existing = await LoadFreshAsync(ids);
        var productIds = existing.ToDictionary(v => v.ProductVariantId, v => v.ProductId);

        await InTransactionAsync(async () =>
        {
            var now = DateTime.UtcNow;

            foreach (var (variantId, quantity) in grouped.OrderBy(g => g.Key))
            {
                if (!productIds.TryGetValue(variantId, out var productId))
                    throw new InvalidOperationException($"Product variant {variantId} not found.");

                var affected = await _context.ProductVariants
                    .Where(v => v.ProductVariantId == variantId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.StockQuantity, v => v.StockQuantity + quantity)
                        .SetProperty(v => v.DateUpdated, v => (DateTime?)now));

                if (affected == 0)
                    throw new InvalidOperationException($"Product variant {variantId} not found.");

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = productId,
                    ProductVariantId = variantId,
                    QuantityChange = quantity,
                    Reason = reason
                });
            }

            await _context.SaveChangesAsync();
        });

        return await LoadFreshAsync(ids);
    }

    // ---------- Helpers ----------

    /// <summary>Merges duplicate variant lines (the same size/colour scanned twice) and validates quantities.</summary>
    private static Dictionary<int, int> Aggregate(IEnumerable<(int VariantId, int Quantity)> lines)
    {
        var result = new Dictionary<int, int>();
        foreach (var (variantId, quantity) in lines)
        {
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(lines), "Stock change quantity must be positive.");
            result[variantId] = result.TryGetValue(variantId, out var current) ? current + quantity : quantity;
        }
        return result;
    }

    /// <summary>Always reads current values from the database - the change tracker can hold stale copies
    /// because the atomic UPDATEs above deliberately bypass it.</summary>
    private async Task<List<ProductVariant>> LoadFreshAsync(List<int> ids) =>
        await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => ids.Contains(v.ProductVariantId))
            .ToListAsync();

    private async Task<Exception> DescribeFailureAsync(int variantId, int requested)
    {
        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.ProductVariantId == variantId);

        if (variant is null)
            return new InvalidOperationException($"Product variant {variantId} not found.");

        return new InsufficientStockException(
            variantId, $"{variant.Product.Name} ({variant.Size}/{variant.Color})", requested, variant.StockQuantity);
    }

    /// <summary>Runs the work in its own transaction unless the caller already has one, in which case the
    /// caller owns commit and rollback.</summary>
    private async Task InTransactionAsync(Func<Task> work)
    {
        if (_context.Database.CurrentTransaction is not null)
        {
            await work();
            return;
        }

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            await work();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            foreach (var entry in _context.ChangeTracker.Entries().Where(e => e.Entity is InventoryTransaction).ToList())
                entry.State = EntityState.Detached; // those rows were rolled back
            throw;
        }
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
