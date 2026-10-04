using System.Security.Cryptography;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

public class DiscountService : IDiscountService
{
    // No 0/O/1/I/L so a code read out over the counter can't be misheard or mistyped.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly ApplicationDbContext _context;

    public DiscountService(ApplicationDbContext context)
    {
        _context = context;
    }

    public string NormalizeCode(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();

    public async Task<DiscountResult> EvaluateAsync(
        string? code,
        IReadOnlyCollection<DiscountLine> lines,
        DiscountChannel channel,
        string? customerId)
    {
        code = NormalizeCode(code);
        if (code.Length == 0)
            return DiscountResult.Fail("Enter a discount code.");

        if (lines.Count == 0)
            return DiscountResult.Fail("Add something to the basket before applying a code.");

        var discount = await _context.Discounts
            .AsNoTracking()
            .Include(d => d.Targets)
            .FirstOrDefaultAsync(d => d.Code == code);

        // Same message for "doesn't exist" and "switched off" so codes can't be probed.
        if (discount is null || !discount.IsActive)
            return DiscountResult.Fail("That discount code isn't valid.");

        return await EvaluateLoadedAsync(discount, lines, channel, customerId);
    }

    public async Task<DiscountResult> EvaluateBestAutoAsync(
        IReadOnlyCollection<DiscountLine> lines,
        DiscountChannel channel,
        string? customerId)
    {
        if (lines.Count == 0)
            return DiscountResult.Fail("Nothing in the basket.");

        var now = DateTime.UtcNow;
        var candidates = await _context.Discounts
            .AsNoTracking()
            .Include(d => d.Targets)
            .Where(d => d.AutoApply
                        && d.IsActive
                        && d.StartsAt <= now
                        && (d.ExpiresAt == null || d.ExpiresAt >= now)
                        && (d.MaxRedemptions == null || d.RedemptionCount < d.MaxRedemptions)
                        && (d.Channel == DiscountChannel.Both || d.Channel == channel))
            .ToListAsync();

        DiscountResult? best = null;
        foreach (var candidate in candidates)
        {
            var result = await EvaluateLoadedAsync(candidate, lines, channel, customerId);
            if (result.IsValid && (best is null || result.Amount > best.Amount))
                best = result;
        }

        return best ?? DiscountResult.Fail("No automatic discount applies.");
    }

    private async Task<DiscountResult> EvaluateLoadedAsync(
        Discount discount,
        IReadOnlyCollection<DiscountLine> lines,
        DiscountChannel channel,
        string? customerId)
    {
        var now = DateTime.UtcNow;

        if (discount.Channel != DiscountChannel.Both && discount.Channel != channel)
            return DiscountResult.Fail(discount.Channel == DiscountChannel.InStore
                ? "That code can only be used in store."
                : "That code can only be used online.");

        if (discount.StartsAt > now)
            return DiscountResult.Fail($"That code isn't active yet - it starts on {discount.StartsAt.ToLocalTime():dd MMM yyyy}.");

        if (discount.ExpiresAt.HasValue && discount.ExpiresAt.Value < now)
            return DiscountResult.Fail("That code has expired.");

        if (discount.MaxRedemptions.HasValue && discount.RedemptionCount >= discount.MaxRedemptions.Value)
            return DiscountResult.Fail("That code has been fully redeemed.");

        if (!string.IsNullOrEmpty(customerId) && discount.MaxUsesPerCustomer.HasValue)
        {
            var used = await _context.DiscountRedemptions
                .CountAsync(r => r.DiscountId == discount.DiscountId && r.CustomerId == customerId);
            if (used >= discount.MaxUsesPerCustomer.Value)
                return DiscountResult.Fail("You've already used this code the maximum number of times.");
        }

        // --- Which part of the basket does it cover? ---
        decimal eligible;
        if (discount.Targets.Count == 0)
        {
            eligible = lines.Sum(l => l.UnitPrice * l.Quantity);
        }
        else
        {
            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await _context.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.Category, p.Brand, p.SupplierId })
                .ToDictionaryAsync(p => p.ProductId);

            eligible = 0m;
            foreach (var line in lines)
            {
                if (!products.TryGetValue(line.ProductId, out var product)) continue;

                var matches = discount.Targets.Any(t => t.Type switch
                {
                    DiscountTargetType.Product => t.ProductId == product.ProductId,
                    DiscountTargetType.Supplier => t.SupplierId.HasValue && t.SupplierId == product.SupplierId,
                    DiscountTargetType.Category => string.Equals(t.Value, product.Category, StringComparison.OrdinalIgnoreCase),
                    DiscountTargetType.Brand => !string.IsNullOrEmpty(product.Brand) && string.Equals(t.Value, product.Brand, StringComparison.OrdinalIgnoreCase),
                    _ => false
                });

                if (matches) eligible += line.UnitPrice * line.Quantity;
            }

            if (eligible <= 0m)
                return DiscountResult.Fail("That code doesn't apply to anything in your basket.");
        }

        if (discount.MinimumSpend.HasValue && eligible < discount.MinimumSpend.Value)
            return DiscountResult.Fail($"Spend at least R{discount.MinimumSpend.Value:0.00} on eligible items to use this code.");

        // --- The saving ---
        var amount = discount.Type == DiscountType.Percentage
            ? eligible * (discount.Value / 100m)
            : discount.Value;

        if (discount.MaxDiscountAmount.HasValue)
            amount = Math.Min(amount, discount.MaxDiscountAmount.Value);

        amount = Math.Min(amount, eligible);
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        if (amount <= 0m)
            return DiscountResult.Fail("That code doesn't save anything on this basket.");

        return new DiscountResult
        {
            IsValid = true,
            Discount = discount,
            Amount = amount,
            EligibleSubtotal = Math.Round(eligible, 2, MidpointRounding.AwayFromZero)
        };
    }

    public async Task RedeemAsync(string code, int orderId, string? customerId, decimal amount)
    {
        code = NormalizeCode(code);

        // One atomic conditional UPDATE: it only succeeds while the code is still on and under its limit, so two
        // simultaneous sales can't both take the last redemption. Joins the caller's transaction automatically.
        var rows = await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE Discounts
               SET RedemptionCount = RedemptionCount + 1
             WHERE Code = {code}
               AND IsActive = 1
               AND (MaxRedemptions IS NULL OR RedemptionCount < MaxRedemptions)");

        if (rows == 0)
            throw new DiscountUnavailableException(code);

        var discountId = await _context.Discounts
            .AsNoTracking()
            .Where(d => d.Code == code)
            .Select(d => d.DiscountId)
            .FirstAsync();

        _context.DiscountRedemptions.Add(new DiscountRedemption
        {
            DiscountId = discountId,
            OrderId = orderId,
            CustomerId = customerId,
            AmountDiscounted = amount,
            DateRedeemed = DateTime.UtcNow
        });
    }

    public async Task<List<Discount>> GetLiveForChannelAsync(DiscountChannel channel)
    {
        var now = DateTime.UtcNow;
        return await _context.Discounts
            .AsNoTracking()
            .Include(d => d.Targets)
            .Where(d => d.IsActive
                        && d.StartsAt <= now
                        && (d.ExpiresAt == null || d.ExpiresAt >= now)
                        && (d.MaxRedemptions == null || d.RedemptionCount < d.MaxRedemptions)
                        && (d.Channel == DiscountChannel.Both || d.Channel == channel))
            .OrderBy(d => d.Name)
            .ThenBy(d => d.Code)
            .ToListAsync();
    }

    public async Task<Discount?> GetActiveBannerAsync()
    {
        var now = DateTime.UtcNow;
        return await _context.Discounts
            .AsNoTracking()
            .Where(d => d.ShowBanner
                        && d.IsActive
                        && d.StartsAt <= now
                        && (d.ExpiresAt == null || d.ExpiresAt >= now)
                        && (d.MaxRedemptions == null || d.RedemptionCount < d.MaxRedemptions)
                        && d.Channel != DiscountChannel.InStore)
            .OrderByDescending(d => d.StartsAt)
            .ThenByDescending(d => d.DiscountId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Discount>> GetLinkedBannerDiscountsAsync(int productId, string category, string? brand, int? supplierId)
    {
        var now = DateTime.UtcNow;
        var candidates = await _context.Discounts
            .AsNoTracking()
            .Include(d => d.Targets)
            .Where(d => d.ShowBanner
                        && d.IsActive
                        && d.StartsAt <= now
                        && (d.ExpiresAt == null || d.ExpiresAt >= now)
                        && (d.MaxRedemptions == null || d.RedemptionCount < d.MaxRedemptions)
                        && d.Channel != DiscountChannel.InStore
                        && d.Targets.Any())
            .ToListAsync();

        return candidates
            .Where(d => d.Targets.Any(t => t.Type switch
            {
                DiscountTargetType.Product => t.ProductId == productId,
                DiscountTargetType.Supplier => t.SupplierId.HasValue && t.SupplierId == supplierId,
                DiscountTargetType.Category => string.Equals(t.Value, category, StringComparison.OrdinalIgnoreCase),
                DiscountTargetType.Brand => !string.IsNullOrEmpty(brand) && string.Equals(t.Value, brand, StringComparison.OrdinalIgnoreCase),
                _ => false
            }))
            .ToList();
    }

    public async Task<Dictionary<int, List<string>>> GetLiveLinkedCodesByProductAsync(IReadOnlyCollection<Product> products)
    {
        var result = new Dictionary<int, List<string>>();
        if (products.Count == 0) return result;

        var now = DateTime.UtcNow;
        var live = await _context.Discounts
            .AsNoTracking()
            .Include(d => d.Targets)
            .Where(d => d.IsActive
                        && d.StartsAt <= now
                        && (d.ExpiresAt == null || d.ExpiresAt >= now)
                        && (d.MaxRedemptions == null || d.RedemptionCount < d.MaxRedemptions)
                        && d.Targets.Any())
            .ToListAsync();

        foreach (var product in products)
        {
            var codes = live
                .Where(d => d.Targets.Any(t => t.Type switch
                {
                    DiscountTargetType.Product => t.ProductId == product.ProductId,
                    DiscountTargetType.Supplier => t.SupplierId.HasValue && t.SupplierId == product.SupplierId,
                    DiscountTargetType.Category => string.Equals(t.Value, product.Category, StringComparison.OrdinalIgnoreCase),
                    DiscountTargetType.Brand => !string.IsNullOrEmpty(product.Brand) && string.Equals(t.Value, product.Brand, StringComparison.OrdinalIgnoreCase),
                    _ => false
                }))
                .Select(d => d.Code)
                .ToList();

            if (codes.Count > 0) result[product.ProductId] = codes;
        }

        return result;
    }

    public async Task<string> GenerateUniqueCodeAsync(string? prefix = null)
    {
        prefix = string.IsNullOrWhiteSpace(prefix)
            ? "FIX"
            : new string(prefix.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).Take(10).ToArray());
        if (prefix.Length == 0) prefix = "FIX";

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];

            var candidate = $"{prefix}-{new string(chars)}";
            if (!await _context.Discounts.AnyAsync(d => d.Code == candidate))
                return candidate;
        }

        // Astronomically unlikely - fall back to a longer suffix rather than ever looping forever.
        return $"{prefix}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
    }
}
