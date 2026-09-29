using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

public class WalletResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal BalanceAfter { get; set; }
    /// <summary>True when CreditFromDepositAsync found this reference already credited and
    /// deliberately did nothing - not a failure, just a no-op. Lets a duplicate Paystack
    /// callback (browser back-button, a retried webhook) show the customer their real balance
    /// instead of double-crediting or reporting an error.</summary>
    public bool AlreadyProcessed { get; set; }

    public static WalletResult Ok(decimal balanceAfter, bool alreadyProcessed = false) =>
        new() { Success = true, BalanceAfter = balanceAfter, AlreadyProcessed = alreadyProcessed };
    public static WalletResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public interface IWalletService
{
    Task<decimal> GetBalanceAsync(string customerId);
    Task<List<WalletTransaction>> GetHistoryAsync(string customerId, int take = 50);
    Task<WalletResult> CreditFromDepositAsync(string customerId, decimal amount, string paystackReference);
    Task<WalletResult> DebitForOrderAsync(string customerId, decimal amount, string reference, int? orderId = null);
    Task LinkOrderAsync(string reference, int orderId);
    Task<WalletResult> CreditRefundAsync(string customerId, decimal amount, int orderId, string note);
}

/// <summary>
/// Every method here runs its read-modify-write against WalletAccount.Balance inside a retry
/// loop keyed off RowVersion (see RunWithRetryAsync) - two requests touching the same wallet at
/// the same moment (a refund landing while the customer is mid-checkout, say) must never let one
/// silently clobber the other. This is more rigor than most of the rest of this app currently
/// has around concurrent writes (ProductVariant.StockQuantity is still an open backlog item for
/// the same class of bug) - real money is exactly where that rigor belongs first.
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

    public async Task<decimal> GetBalanceAsync(string customerId)
    {
        var account = await _context.WalletAccounts.AsNoTracking().FirstOrDefaultAsync(w => w.CustomerId == customerId);
        return account?.Balance ?? 0m;
    }

    public async Task<List<WalletTransaction>> GetHistoryAsync(string customerId, int take = 50)
    {
        var account = await _context.WalletAccounts.AsNoTracking().FirstOrDefaultAsync(w => w.CustomerId == customerId);
        if (account is null) return new List<WalletTransaction>();

        return await _context.WalletTransactions
            .AsNoTracking()
            .Where(t => t.WalletAccountId == account.WalletAccountId)
            .OrderByDescending(t => t.DateCreated)
            .Take(take)
            .ToListAsync();
    }

    public async Task<WalletResult> CreditFromDepositAsync(string customerId, decimal amount, string paystackReference)
    {
        if (amount <= 0) return WalletResult.Fail("Deposit amount must be greater than zero.");

        // Idempotency check happens OUTSIDE the retry loop and before any account is even
        // created - a duplicate callback for a customer's very first deposit must still be
        // recognised as a duplicate, not treated as "no account yet, so nothing to conflict
        // with".
        var alreadyCredited = await _context.WalletTransactions
            .AsNoTracking()
            .AnyAsync(t => t.Type == WalletTransactionType.Deposit && t.Reference == paystackReference);
        if (alreadyCredited)
        {
            var existingBalance = await GetBalanceAsync(customerId);
            _logger.LogInformation("Wallet deposit {Reference} already credited - ignoring duplicate callback.", paystackReference);
            return WalletResult.Ok(existingBalance, alreadyProcessed: true);
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
        });
    }

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
        });
    }

    public async Task LinkOrderAsync(string reference, int orderId)
    {
        // Called right after CreateOnlineOrderAsync succeeds, to backfill the OrderId onto the
        // Spend ledger row that DebitForOrderAsync wrote before the order existed (debit has to
        // happen FIRST - see ShopController.Checkout - so a failed debit never leaves a
        // stock-decremented, unpaid order behind, same principle as the saved-card charge path).
        var txn = await _context.WalletTransactions.FirstOrDefaultAsync(t => t.Reference == reference && t.Type == WalletTransactionType.Spend);
        if (txn is not null)
        {
            txn.OrderId = orderId;
            await _context.SaveChangesAsync();
        }
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
                Note = note
            });
        });
    }

    /// <summary>Loads (creating if needed) the customer's tracked WalletAccount, lets the caller
    /// mutate its Balance and add exactly one WalletTransaction, and saves - retrying from a
    /// fresh read if SaveChangesAsync reports a concurrency conflict (another request updated
    /// the same row first, via the RowVersion token). A mutation that decides there isn't enough
    /// balance should throw InvalidOperationException, which this turns into a normal
    /// WalletResult.Fail rather than a crash.</summary>
    private async Task<WalletResult> RunWithRetryAsync(string customerId, Action<WalletAccount> mutate)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var account = await _context.WalletAccounts.FirstOrDefaultAsync(w => w.CustomerId == customerId);
            if (account is null)
            {
                account = new WalletAccount { CustomerId = customerId };
                _context.WalletAccounts.Add(account);
                // Needed so account.WalletAccountId is populated before mutate() uses it below,
                // without yet committing - a brand new wallet can't hit a concurrency conflict
                // on its own insert, so this extra round-trip is safe.
                await _context.SaveChangesAsync();
            }

            try
            {
                mutate(account);
                await _context.SaveChangesAsync();
                return WalletResult.Ok(account.Balance);
            }
            catch (InvalidOperationException ex)
            {
                // A business-rule failure (insufficient balance) from inside mutate() - not a
                // concurrency conflict, don't retry, just report it.
                foreach (var entry in _context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                    entry.State = EntityState.Unchanged; // discard the uncommitted mutation
                return WalletResult.Fail(ex.Message);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxRetries)
            {
                _logger.LogWarning("Wallet concurrency conflict for customer {CustomerId}, attempt {Attempt} - retrying.", customerId, attempt);
                foreach (var entry in _context.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached; // force a completely fresh read next loop
            }
        }

        return WalletResult.Fail("Could not update your FixCash balance right now - please try again.");
    }
}
