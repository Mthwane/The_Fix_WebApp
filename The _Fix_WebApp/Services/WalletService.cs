using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

public class WalletResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal BalanceAfter { get; set; }
    /// <summary>How much this call actually moved (0 when it was a no-op).</summary>
    public decimal Amount { get; set; }
    public bool AlreadyProcessed { get; set; }

    public static WalletResult Ok(decimal balanceAfter, bool alreadyProcessed = false, decimal amount = 0m) =>
        new() { Success = true, BalanceAfter = balanceAfter, AlreadyProcessed = alreadyProcessed, Amount = amount };
    public static WalletResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public interface IWalletService
{
    Task<decimal> GetBalanceAsync(string customerId);
    Task<List<WalletTransaction>> GetHistoryAsync(string customerId, int take = 50);
    Task<(List<WalletTransaction> Items, int TotalCount)> GetHistoryPageAsync(string customerId, int page, int pageSize);
    Task<WalletResult> CreditFromDepositAsync(string customerId, decimal amount, string paystackReference);
    Task<WalletResult> DebitForOrderAsync(string customerId, decimal amount, string reference, int? orderId = null);
    /// <summary>Undoes a Spend made with this reference (payment failed after the wallet was debited). Idempotent.</summary>
    Task<WalletResult> RestoreDebitAsync(string customerId, string reference);
    Task LinkOrderAsync(string reference, int orderId);
    Task<WalletResult> CreditRefundAsync(string customerId, decimal amount, int orderId, string note);
    /// <summary>Credits the wallet for a processed return. Idempotent per returnId (reference RETURN-{returnId}).</summary>
    Task<WalletResult> CreditReturnAsync(string customerId, decimal amount, int orderId, int returnId, string note);
    /// <summary>Returns an order's FixCash portion to the wallet when the order is cancelled. Idempotent.</summary>
    Task<WalletResult> RefundCancelledOrderAsync(Order order);
    /// <summary>Staff correction: positive = credit, negative = debit. Requires a note.</summary>
    Task<WalletResult> AdjustAsync(string customerId, decimal signedAmount, string note, string staffUserId);
}

/// <summary>
/// Every method runs its read-modify-write against WalletAccount.Balance inside a retry loop keyed off
/// RowVersion (see RunWithRetryAsync). The ledger is append-only; corrections are new rows.
/// </summary>
public class WalletService : IWalletService
{
    private const int MaxRetries = 3;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<WalletService> _logger;

    public WalletService(ApplicationDbContext context, ILogger<WalletService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ---------- Reads ----------

    public async Task<decimal> GetBalanceAsync(string customerId)
    {
        var account = await _context.WalletAccounts.AsNoTracking().FirstOrDefaultAsync(w => w.CustomerId == customerId);
        return account?.Balance ?? 0m;
    }

    public async Task<List<WalletTransaction>> GetHistoryAsync(string customerId, int take = 50) =>
        (await GetHistoryPageAsync(customerId, 1, take)).Items;

    public async Task<(List<WalletTransaction> Items, int TotalCount)> GetHistoryPageAsync(string customerId, int page, int pageSize)
    {
        var account = await _context.WalletAccounts.AsNoTracking().FirstOrDefaultAsync(w => w.CustomerId == customerId);
        if (account is null) return (new List<WalletTransaction>(), 0);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _context.WalletTransactions.AsNoTracking().Where(t => t.WalletAccountId == account.WalletAccountId);
        var total = await query.CountAsync();
        var items = await query
            .Include(t => t.Order)
            .OrderByDescending(t => t.DateCreated).ThenByDescending(t => t.WalletTransactionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    // ---------- Credits ----------

    public async Task<WalletResult> CreditFromDepositAsync(string customerId, decimal amount, string paystackReference)
    {
        if (amount <= 0) return WalletResult.Fail("Deposit amount must be greater than zero.");

        var alreadyCredited = await _context.WalletTransactions.AsNoTracking()
            .AnyAsync(t => t.Type == WalletTransactionType.Deposit && t.Reference == paystackReference);
        if (alreadyCredited)
        {
            _logger.LogInformation("Wallet deposit {Reference} already credited - ignoring duplicate callback.", paystackReference);
            return WalletResult.Ok(await GetBalanceAsync(customerId), alreadyProcessed: true);
        }

        return await RunWithRetryAsync(customerId, account =>
        {
            account.Balance += amount;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletAccountId = account.WalletAccountId,
                Type = WalletTransactionType.Deposit,
                Amount = amount,
                BalanceAfter = account.Balance,
                Reference = paystackReference
            });
        }, amount);
    }

    public async Task<WalletResult> CreditRefundAsync(string customerId, decimal amount, int orderId, string note)
    {
        if (amount <= 0) return WalletResult.Fail("Refund amount must be greater than zero.");

        return await RunWithRetryAsync(customerId, account =>
        {
            account.Balance += amount;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletAccountId = account.WalletAccountId,
                Type = WalletTransactionType.RefundCredit,
                Amount = amount,
                BalanceAfter = account.Balance,
                OrderId = orderId,
                Note = Truncate(note, 250)
            });
        }, amount);
    }

    public async Task<WalletResult> CreditReturnAsync(string customerId, decimal amount, int orderId, int returnId, string note) =>
        await CreditOnceAsync(customerId, amount, orderId, $"RETURN-{returnId}", note);

    public async Task<WalletResult> RefundCancelledOrderAsync(Order order)
    {
        if (string.IsNullOrEmpty(order.CustomerId) || order.WalletAmountApplied <= 0)
            return WalletResult.Ok(0m);

        return await CreditOnceAsync(order.CustomerId, order.WalletAmountApplied, order.OrderId,
            $"CANCEL-{order.OrderNumber}", $"Refund for cancelled order {order.OrderNumber}");
    }

    public async Task<WalletResult> RestoreDebitAsync(string customerId, string reference)
    {
        var spend = await _context.WalletTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Type == WalletTransactionType.Spend
                && t.Reference == reference
                && t.WalletAccount!.CustomerId == customerId);
        if (spend is null) return WalletResult.Fail("No wallet payment found for that reference.");

        return await CreditOnceAsync(customerId, spend.Amount, spend.OrderId ?? 0,
            $"REV-{reference}", "Payment did not complete - FixCash returned", orderIdIsOptional: true);
    }

    /// <summary>Credits once per reference (a filtered unique index on Reference+Type backs this up at DB level).</summary>
    private async Task<WalletResult> CreditOnceAsync(string customerId, decimal amount, int orderId, string reference, string note, bool orderIdIsOptional = false)
    {
        if (amount <= 0) return WalletResult.Fail("Refund amount must be greater than zero.");

        var done = await _context.WalletTransactions.AsNoTracking()
            .AnyAsync(t => t.Type == WalletTransactionType.RefundCredit && t.Reference == reference);
        if (done) return WalletResult.Ok(await GetBalanceAsync(customerId), alreadyProcessed: true);

        return await RunWithRetryAsync(customerId, account =>
        {
            account.Balance += amount;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletAccountId = account.WalletAccountId,
                Type = WalletTransactionType.RefundCredit,
                Amount = amount,
                BalanceAfter = account.Balance,
                Reference = reference,
                OrderId = orderIdIsOptional && orderId == 0 ? null : orderId,
                Note = Truncate(note, 250)
            });
        }, amount);
    }

    // ---------- Debits ----------

    public async Task<WalletResult> DebitForOrderAsync(string customerId, decimal amount, string reference, int? orderId = null)
    {
        if (amount <= 0) return WalletResult.Fail("Debit amount must be greater than zero.");

        return await RunWithRetryAsync(customerId, account =>
        {
            if (account.Balance < amount)
                throw new InvalidOperationException($"Insufficient FixCash balance (R{account.Balance:N2} available, R{amount:N2} needed).");

            account.Balance -= amount;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletAccountId = account.WalletAccountId,
                Type = WalletTransactionType.Spend,
                Amount = amount,
                BalanceAfter = account.Balance,
                Reference = reference,
                OrderId = orderId
            });
        }, amount);
    }

    public async Task LinkOrderAsync(string reference, int orderId)
    {
        var txn = await _context.WalletTransactions.FirstOrDefaultAsync(t => t.Reference == reference && t.Type == WalletTransactionType.Spend);
        if (txn is not null && txn.OrderId is null)
        {
            txn.OrderId = orderId;
            await _context.SaveChangesAsync();
        }
    }

    // ---------- Staff adjustments ----------

    public async Task<WalletResult> AdjustAsync(string customerId, decimal signedAmount, string note, string staffUserId)
    {
        signedAmount = Math.Round(signedAmount, 2, MidpointRounding.AwayFromZero);
        if (signedAmount == 0m) return WalletResult.Fail("Adjustment amount can't be zero.");
        if (string.IsNullOrWhiteSpace(note)) return WalletResult.Fail("A reason is required for every adjustment.");

        var amount = Math.Abs(signedAmount);
        var isCredit = signedAmount > 0;

        return await RunWithRetryAsync(customerId, account =>
        {
            if (!isCredit && account.Balance < amount)
                throw new InvalidOperationException($"Can't debit R{amount:N2} - the wallet only holds R{account.Balance:N2}.");

            account.Balance += isCredit ? amount : -amount;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletAccountId = account.WalletAccountId,
                Type = isCredit ? WalletTransactionType.AdminAdjustment : WalletTransactionType.AdminDebit,
                Amount = amount,
                BalanceAfter = account.Balance,
                Reference = $"ADJ-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                Note = Truncate(note.Trim(), 250),
                CreatedByUserId = staffUserId
            });
        }, amount);
    }

    // ---------- Concurrency ----------

    private async Task<WalletResult> RunWithRetryAsync(string customerId, Action<WalletAccount> mutate, decimal moved = 0m)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var account = await _context.WalletAccounts.FirstOrDefaultAsync(w => w.CustomerId == customerId);
            if (account is null)
            {
                account = new WalletAccount { CustomerId = customerId };
                _context.WalletAccounts.Add(account);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Another request created this customer's wallet first (unique index) - re-read.
                    DetachWalletEntries();
                    if (attempt == MaxRetries) break;
                    continue;
                }
            }

            try
            {
                mutate(account);
                await _context.SaveChangesAsync();
                return WalletResult.Ok(account.Balance, amount: moved);
            }
            catch (InvalidOperationException ex)
            {
                DetachWalletEntries();
                return WalletResult.Fail(ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachWalletEntries();
                if (attempt == MaxRetries) break;
                _logger.LogWarning("Wallet concurrency conflict for customer {CustomerId}, attempt {Attempt} - retrying.", customerId, attempt);
            }
            catch (DbUpdateException ex)
            {
                DetachWalletEntries();
                _logger.LogError(ex, "Wallet ledger write failed for customer {CustomerId}.", customerId);
                return WalletResult.Fail("Could not record that FixCash movement.");
            }
        }

        return WalletResult.Fail("Could not update the FixCash balance right now - please try again.");
    }

    /// <summary>Only discards wallet rows - never other tracked entities (an Order mid-cancel, say).</summary>
    private void DetachWalletEntries()
    {
        foreach (var entry in _context.ChangeTracker.Entries()
                     .Where(e => e.Entity is WalletAccount or WalletTransaction)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
