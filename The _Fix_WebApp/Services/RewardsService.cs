using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

public class RewardsResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public bool AlreadyProcessed { get; set; }

    public static RewardsResult Ok(int points = 0, int balanceAfter = 0, bool alreadyProcessed = false) =>
        new() { Success = true, Points = points, BalanceAfter = balanceAfter, AlreadyProcessed = alreadyProcessed };
    public static RewardsResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public interface IRewardsService
{
    Task<RewardsSettings> GetSettingsAsync();
    Task<int> GetBalanceAsync(string customerId);
    Task<List<RewardsTransaction>> GetHistoryAsync(string customerId, int take = 50);
    Task<RewardsOverview> GetOverviewAsync();

    int CalculateEarnedPoints(RewardsSettings settings, decimal qualifyingAmount);
    decimal PointsToRands(RewardsSettings settings, int points);
    RedemptionQuote GetRedemptionQuote(RewardsSettings settings, int balance, decimal orderSubTotal);
    string DescribeEarnRule(RewardsSettings settings);

    Task<RewardsResult> EarnForOrderAsync(Order order);
    Task<RewardsResult> RedeemAsync(string customerId, int points, string reference);
    Task<RewardsResult> RestoreRedemptionAsync(string customerId, string reference);
    Task LinkOrderAsync(string reference, int orderId);
    Task<RewardsResult> ReverseEarnedForReturnAsync(int orderId, string reference);
    Task<RewardsResult> ReverseForCancelledOrderAsync(int orderId);
}

/// <summary>
/// Points ledger. Same concurrency pattern as WalletService (RowVersion + bounded retry), except
/// the retry only detaches Rewards* entries rather than everything in the change tracker.
/// </summary>
public class RewardsService : IRewardsService
{
    private const int MaxRetries = 3;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<RewardsService> _logger;

    public RewardsService(ApplicationDbContext context, ILogger<RewardsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ---------- Reads ----------

    public async Task<RewardsSettings> GetSettingsAsync() =>
        await _context.RewardsSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1)
        ?? new RewardsSettings { Id = 1 }; // disabled defaults

    public async Task<int> GetBalanceAsync(string customerId)
    {
        var account = await _context.RewardsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == customerId);
        return account?.Balance ?? 0;
    }

    public async Task<List<RewardsTransaction>> GetHistoryAsync(string customerId, int take = 50)
    {
        var account = await _context.RewardsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == customerId);
        if (account is null) return new List<RewardsTransaction>();

        return await _context.RewardsTransactions
            .AsNoTracking()
            .Include(t => t.Order)
            .Where(t => t.RewardsAccountId == account.RewardsAccountId)
            .OrderByDescending(t => t.DateCreated)
            .Take(take)
            .ToListAsync();
    }

    public async Task<RewardsOverview> GetOverviewAsync()
    {
        var settings = await GetSettingsAsync();
        var accounts = _context.RewardsAccounts.AsNoTracking();
        var outstanding = await accounts.SumAsync(a => (long?)a.Balance) ?? 0L;
        var members = await accounts.CountAsync(a => a.Balance > 0);

        var since = DateTime.UtcNow.AddDays(-30);
        var ledger = _context.RewardsTransactions.AsNoTracking().Where(t => t.DateCreated >= since);
        var earned = await ledger.Where(t => t.Type == RewardsTransactionType.Earned).SumAsync(t => (long?)t.Points) ?? 0L;
        var redeemed = await ledger.Where(t => t.Type == RewardsTransactionType.Redeemed).SumAsync(t => (long?)t.Points) ?? 0L;

        return new RewardsOverview
        {
            MembersWithPoints = members,
            OutstandingPoints = outstanding,
            OutstandingLiability = Math.Round(outstanding * settings.PointValueRands, 2, MidpointRounding.AwayFromZero),
            EarnedLast30Days = earned,
            RedeemedLast30Days = redeemed
        };
    }

    // ---------- Pure calculations ----------

    public int CalculateEarnedPoints(RewardsSettings settings, decimal qualifyingAmount)
    {
        if (!settings.IsEnabled || qualifyingAmount <= 0 || qualifyingAmount < settings.MinimumOrderAmount)
            return 0;

        decimal raw = settings.EarnMode switch
        {
            RewardEarnMode.FlatPointsPerPurchase => settings.FlatPointsPerPurchase,
            RewardEarnMode.PointsPerRandSpent => qualifyingAmount * settings.PointsPerRand,
            RewardEarnMode.CashBackPercentage => settings.PointValueRands > 0
                ? qualifyingAmount * settings.CashBackPercentage / 100m / settings.PointValueRands
                : 0m,
            _ => 0m
        };

        return (int)Math.Floor(Math.Min(Math.Max(raw, 0m), 1_000_000_000m));
    }

    public decimal PointsToRands(RewardsSettings settings, int points) =>
        Math.Round(points * settings.PointValueRands, 2, MidpointRounding.AwayFromZero);

    public RedemptionQuote GetRedemptionQuote(RewardsSettings settings, int balance, decimal orderSubTotal)
    {
        var quote = new RedemptionQuote { Balance = balance };

        if (!settings.IsEnabled)
        {
            quote.Reason = "The rewards programme is currently paused.";
            return quote;
        }
        if (settings.PointValueRands <= 0 || orderSubTotal <= 0) return quote;

        if (balance < settings.MinimumPointsToRedeem)
        {
            quote.Reason = $"You need at least {settings.MinimumPointsToRedeem} points to redeem (you have {balance}).";
            return quote;
        }

        var maxDiscount = orderSubTotal * settings.MaxRedeemPercentOfOrder / 100m;
        var maxByOrder = (int)Math.Floor(Math.Min(maxDiscount / settings.PointValueRands, 1_000_000_000m));
        var maxPoints = Math.Min(balance, maxByOrder);
        if (maxPoints <= 0) return quote;

        quote.CanRedeem = true;
        quote.MaxPoints = maxPoints;
        quote.MaxDiscount = Math.Min(PointsToRands(settings, maxPoints), orderSubTotal);
        return quote;
    }

    public string DescribeEarnRule(RewardsSettings settings)
    {
        if (!settings.IsEnabled) return "The rewards programme is currently paused.";

        var minText = settings.MinimumOrderAmount > 0
            ? $" (minimum spend {settings.MinimumOrderAmount:C}, before VAT)"
            : "";

        switch (settings.EarnMode)
        {
            case RewardEarnMode.FlatPointsPerPurchase:
                return $"Earn {settings.FlatPointsPerPurchase} points on every purchase{minText}.";
            case RewardEarnMode.PointsPerRandSpent:
                if (settings.PointsPerRand <= 0) return "Earning is not configured yet.";
                return settings.PointsPerRand >= 1
                    ? $"Earn {settings.PointsPerRand:0.##} points for every R1 you spend{minText}."
                    : $"Earn 1 point for every {(1m / settings.PointsPerRand):C} you spend{minText}.";
            case RewardEarnMode.CashBackPercentage:
                return $"Earn {settings.CashBackPercentage:0.##}% back in points{minText}.";
            default:
                return string.Empty;
        }
    }

    // ---------- Earning ----------

    public async Task<RewardsResult> EarnForOrderAsync(Order order)
    {
        if (string.IsNullOrEmpty(order.CustomerId)) return RewardsResult.Ok();

        var settings = await GetSettingsAsync();
        var points = CalculateEarnedPoints(settings, order.SubTotal - order.DiscountTotal);
        if (points <= 0) return RewardsResult.Ok();

        var alreadyEarned = await _context.RewardsTransactions.AsNoTracking()
            .AnyAsync(t => t.OrderId == order.OrderId && t.Type == RewardsTransactionType.Earned);
        if (alreadyEarned) return RewardsResult.Ok(alreadyProcessed: true);

        return await RunWithRetryAsync(order.CustomerId, account =>
        {
            account.Balance += points;
            _context.RewardsTransactions.Add(new RewardsTransaction
            {
                RewardsAccountId = account.RewardsAccountId,
                Type = RewardsTransactionType.Earned,
                Points = points,
                BalanceAfter = account.Balance,
                Reference = order.OrderNumber,
                OrderId = order.OrderId,
                Note = $"Earned on order {order.OrderNumber}"
            });
            return points;
        });
    }

    // ---------- Redemption ----------

    public async Task<RewardsResult> RedeemAsync(string customerId, int points, string reference)
    {
        if (points <= 0) return RewardsResult.Fail("Points to redeem must be greater than zero.");

        var alreadyRedeemed = await _context.RewardsTransactions.AsNoTracking()
            .AnyAsync(t => t.Type == RewardsTransactionType.Redeemed && t.Reference == reference);
        if (alreadyRedeemed) return RewardsResult.Ok(alreadyProcessed: true);

        return await RunWithRetryAsync(customerId, account =>
        {
            if (account.Balance < points)
                throw new InvalidOperationException($"Not enough points ({account.Balance} available, {points} needed).");

            account.Balance -= points;
            _context.RewardsTransactions.Add(new RewardsTransaction
            {
                RewardsAccountId = account.RewardsAccountId,
                Type = RewardsTransactionType.Redeemed,
                Points = points,
                BalanceAfter = account.Balance,
                Reference = reference,
                Note = "Redeemed at checkout"
            });
            return points;
        });
    }

    public async Task<RewardsResult> RestoreRedemptionAsync(string customerId, string reference)
    {
        var redeemed = await _context.RewardsTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Type == RewardsTransactionType.Redeemed
                && t.Reference == reference
                && t.RewardsAccount!.CustomerId == customerId);
        if (redeemed is null) return RewardsResult.Fail("No redemption found for that reference.");

        var alreadyRestored = await _context.RewardsTransactions.AsNoTracking()
            .AnyAsync(t => t.Type == RewardsTransactionType.RedemptionRestored && t.Reference == reference);
        if (alreadyRestored) return RewardsResult.Ok(alreadyProcessed: true);

        var points = redeemed.Points;
        var orderId = redeemed.OrderId;

        return await RunWithRetryAsync(customerId, account =>
        {
            account.Balance += points;
            _context.RewardsTransactions.Add(new RewardsTransaction
            {
                RewardsAccountId = account.RewardsAccountId,
                Type = RewardsTransactionType.RedemptionRestored,
                Points = points,
                BalanceAfter = account.Balance,
                Reference = reference,
                OrderId = orderId,
                Note = "Points returned"
            });
            return points;
        });
    }

    /// <summary>Backfills OrderId onto the Redeemed row written before the order existed
    /// (same debit-first ordering as the wallet - see ShopController.Checkout).</summary>
    public async Task LinkOrderAsync(string reference, int orderId)
    {
        var txn = await _context.RewardsTransactions
            .FirstOrDefaultAsync(t => t.Type == RewardsTransactionType.Redeemed && t.Reference == reference);
        if (txn is not null && txn.OrderId is null)
        {
            txn.OrderId = orderId;
            await _context.SaveChangesAsync();
        }
    }

    // ---------- Reversals ----------

    /// <summary>Takes back earned points in proportion to how much of the order's net value has been
    /// refunded so far. Call AFTER the ReturnTransaction is saved - it sums refunds from the database.</summary>
    public async Task<RewardsResult> ReverseEarnedForReturnAsync(int orderId, string reference)
    {
        var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order is null || string.IsNullOrEmpty(order.CustomerId)) return RewardsResult.Ok();

        var net = order.SubTotal - order.DiscountTotal;
        if (net <= 0) return RewardsResult.Ok();

        var refunded = await _context.ReturnTransactions.AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .SumAsync(r => (decimal?)r.RefundAmount) ?? 0m;

        return await ReverseEarnedCoreAsync(order, refunded / net, reference);
    }

    public async Task<RewardsResult> ReverseForCancelledOrderAsync(int orderId)
    {
        var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order is null || string.IsNullOrEmpty(order.CustomerId)) return RewardsResult.Ok();

        var reversal = await ReverseEarnedCoreAsync(order, 1m, $"CANCEL-{order.OrderNumber}");

        // Orders paid without points have no redemption row - a Fail("No redemption found") here is expected.
        await RestoreRedemptionAsync(order.CustomerId, order.OrderNumber);

        return reversal;
    }

    /// <summary>Reverses (fraction x earned) minus whatever was already reversed. Never drives a balance
    /// negative: if the customer has already spent the points, it takes what's left and notes the shortfall
    /// on the ledger row.</summary>
    private async Task<RewardsResult> ReverseEarnedCoreAsync(Order order, decimal fraction, string reference)
    {
        var alreadyDone = await _context.RewardsTransactions.AsNoTracking()
            .AnyAsync(t => t.Type == RewardsTransactionType.EarnReversed && t.Reference == reference);
        if (alreadyDone) return RewardsResult.Ok(alreadyProcessed: true);

        var earned = await _context.RewardsTransactions.AsNoTracking()
            .Where(t => t.OrderId == order.OrderId && t.Type == RewardsTransactionType.Earned)
            .SumAsync(t => (int?)t.Points) ?? 0;
        if (earned <= 0) return RewardsResult.Ok();

        var alreadyReversed = await _context.RewardsTransactions.AsNoTracking()
            .Where(t => t.OrderId == order.OrderId && t.Type == RewardsTransactionType.EarnReversed)
            .SumAsync(t => (int?)t.Points) ?? 0;

        var clamped = Math.Min(1m, Math.Max(0m, fraction));
        var target = (int)Math.Round(earned * clamped, MidpointRounding.AwayFromZero);
        var owed = target - alreadyReversed;
        if (owed <= 0) return RewardsResult.Ok();

        return await RunWithRetryAsync(order.CustomerId!, account =>
        {
            var take = Math.Min(owed, account.Balance);
            if (take <= 0) return 0;

            account.Balance -= take;
            _context.RewardsTransactions.Add(new RewardsTransaction
            {
                RewardsAccountId = account.RewardsAccountId,
                Type = RewardsTransactionType.EarnReversed,
                Points = take,
                BalanceAfter = account.Balance,
                Reference = reference,
                OrderId = order.OrderId,
                Note = take < owed
                    ? $"Reversed {take} of {owed} points on order {order.OrderNumber} (balance was lower)"
                    : $"Points reversed on order {order.OrderNumber}"
            });
            return take;
        });
    }

    // ---------- Concurrency ----------

    /// <summary>Loads (creating if needed) the customer's account, lets the caller mutate Balance and add one
    /// ledger row (returning the points moved), then saves - retrying from a fresh read on a RowVersion conflict.
    /// A mutate that throws InvalidOperationException becomes a normal RewardsResult.Fail.</summary>
    private async Task<RewardsResult> RunWithRetryAsync(string customerId, Func<RewardsAccount, int> mutate)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var account = await _context.RewardsAccounts.FirstOrDefaultAsync(a => a.CustomerId == customerId);
            if (account is null)
            {
                account = new RewardsAccount { CustomerId = customerId };
                _context.RewardsAccounts.Add(account);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Another request created this customer's account first (unique index) - re-read.
                    DetachRewardsEntries();
                    if (attempt == MaxRetries) break;
                    continue;
                }
            }

            try
            {
                var moved = mutate(account);
                await _context.SaveChangesAsync();
                return RewardsResult.Ok(moved, account.Balance);
            }
            catch (InvalidOperationException ex)
            {
                DetachRewardsEntries();
                return RewardsResult.Fail(ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachRewardsEntries();
                if (attempt == MaxRetries) break;
                _logger.LogWarning("Rewards concurrency conflict for customer {CustomerId}, attempt {Attempt} - retrying.", customerId, attempt);
            }
            catch (DbUpdateException ex)
            {
                DetachRewardsEntries();
                _logger.LogError(ex, "Rewards ledger write failed for customer {CustomerId}.", customerId);
                return RewardsResult.Fail("Could not record that rewards movement.");
            }
        }

        return RewardsResult.Fail("Could not update the rewards balance right now - please try again.");
    }

    private void DetachRewardsEntries()
    {
        foreach (var entry in _context.ChangeTracker.Entries()
                     .Where(e => e.Entity is RewardsAccount or RewardsTransaction)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}