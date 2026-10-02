using System.Globalization;
using System.Text;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Staff side of FixCash: look up any customer's wallet and ledger, make manual credits/debits, and
/// see what the business owes in total. Viewing needs wallet.view; changing a balance needs
/// wallet.adjust. Every adjustment is written to the wallet ledger AND the audit log.
/// </summary>
[Authorize(Policy = Permissions.WalletView)]
public class WalletAdminController : Controller
{
    private const int ListPageSize = 25;
    private const int HistoryPageSize = 25;
    private const decimal MaxAdjustment = 50000m;
    private const int DormantDays = 90;

    // South Africa has no daylight saving, so a fixed +2h offset is exact and avoids
    // Windows-vs-Linux time zone ID differences once this is hosted.
    private static readonly TimeSpan SaOffset = TimeSpan.FromHours(2);

    private readonly ApplicationDbContext _context;
    private readonly IWalletService _wallet;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICustomerNotificationService _notify;
    private readonly ILogger<WalletAdminController> _logger;

    public WalletAdminController(
        ApplicationDbContext context,
        IWalletService wallet,
        UserManager<ApplicationUser> userManager,
        ICustomerNotificationService notify,
        ILogger<WalletAdminController> logger)
    {
        _context = context;
        _wallet = wallet;
        _userManager = userManager;
        _notify = notify;
        _logger = logger;
    }

    // GET: /WalletAdmin?search=&page=1
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        page = Math.Max(1, page);

        var query = _context.WalletAccounts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(w => w.Customer != null &&
                (w.Customer.FullName.Contains(term) ||
                 (w.Customer.Email != null && w.Customer.Email.Contains(term))));
        }

        var total = await query.CountAsync();
        var totalBalance = await query.SumAsync(w => (decimal?)w.Balance) ?? 0m;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)ListPageSize));
        if (page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(w => w.Balance)
            .ThenBy(w => w.WalletAccountId)
            .Skip((page - 1) * ListPageSize)
            .Take(ListPageSize)
            .Select(w => new WalletAdminListItem
            {
                CustomerId = w.CustomerId,
                FullName = w.Customer != null ? w.Customer.FullName : "(removed user)",
                Email = w.Customer != null ? w.Customer.Email : null,
                Balance = w.Balance,
                LastActivity = w.Transactions.Max(t => (DateTime?)t.DateCreated)
            })
            .ToListAsync();

        return View(new WalletAdminIndexViewModel
        {
            Items = items,
            Search = search,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total,
            TotalBalance = totalBalance
        });
    }

    // GET: /WalletAdmin/Details/{customerId}?page=1
    [HttpGet]
    public async Task<IActionResult> Details(string id, int page = 1)
    {
        var customer = await _userManager.FindByIdAsync(id);
        if (customer is null) return NotFound();

        page = Math.Max(1, page);
        var (items, total) = await _wallet.GetHistoryPageAsync(id, page, HistoryPageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)HistoryPageSize));

        if (page > totalPages)
        {
            page = totalPages;
            (items, total) = await _wallet.GetHistoryPageAsync(id, page, HistoryPageSize);
        }

        return View(new WalletAdminDetailsViewModel
        {
            Customer = customer,
            Balance = await _wallet.GetBalanceAsync(id),
            Items = items,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total,
            CanAdjust = User.HasClaim(Permissions.ClaimType, Permissions.WalletAdjust)
        });
    }

    // POST: /WalletAdmin/Adjust
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.WalletAdjust)]
    public async Task<IActionResult> Adjust(string id, decimal amount, string direction, string? note)
    {
        var customer = await _userManager.FindByIdAsync(id);
        if (customer is null) return NotFound();

        var staffId = _userManager.GetUserId(User)!;

        if (id == staffId)
        {
            this.ToastError("You can't adjust your own wallet.");
            return RedirectToAction(nameof(Details), new { id });
        }

        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var isDebit = string.Equals(direction, "debit", StringComparison.OrdinalIgnoreCase);

        if (amount <= 0 || amount > MaxAdjustment)
        {
            this.ToastError($"Enter an amount between R0.01 and {MaxAdjustment:N0}.");
            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length < 5)
        {
            this.ToastError("A reason of at least 5 characters is required for every adjustment.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var reason = note.Trim();
        var result = await _wallet.AdjustAsync(id, isDebit ? -amount : amount, reason, staffId);

        if (!result.Success)
        {
            this.ToastError(result.ErrorMessage ?? "The adjustment could not be applied.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var details =
            $"{(isDebit ? "Debited" : "Credited")} {amount:0.00} {(isDebit ? "from" : "to")} the FixCash wallet of " +
            $"{customer.Email}. New balance {result.BalanceAfter:0.00}. Reason: {reason}";

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = staffId,
            Action = "WalletAdjusted",
            Details = details.Length <= 500 ? details : details[..500]
        });
        await _context.SaveChangesAsync();

        await _notify.WalletAdjustedAsync(customer, isDebit ? -amount : amount, result.BalanceAfter, reason);

        this.ToastSuccess(
            $"{amount:C} {(isDebit ? "removed from" : "added to")} {customer.FullName}'s wallet. New balance {result.BalanceAfter:C}.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // GET: /WalletAdmin/Liability?from=2026-09-01&to=2026-09-30
    [HttpGet]
    public async Task<IActionResult> Liability(DateTime? from, DateTime? to)
    {
        var todaySa = (DateTime.UtcNow + SaOffset).Date;
        var toDate = (to ?? todaySa).Date;
        var fromDate = (from ?? toDate.AddDays(-29)).Date;
        if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);

        // Local SA dates -> UTC instants for the query (end is exclusive).
        var fromUtc = DateTime.SpecifyKind(fromDate - SaOffset, DateTimeKind.Utc);
        var toUtcExclusive = DateTime.SpecifyKind(toDate.AddDays(1) - SaOffset, DateTimeKind.Utc);

        var accounts = _context.WalletAccounts.AsNoTracking();

        var totalLiability = await accounts.SumAsync(w => (decimal?)w.Balance) ?? 0m;
        var totalWallets = await accounts.CountAsync();
        var withBalance = await accounts.CountAsync(w => w.Balance > 0);

        // Whole-ledger totals by type: the ledger's net must equal what the balances say.
        var allTime = await _context.WalletTransactions.AsNoTracking()
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync();

        decimal Sum(WalletTransactionType type) => allTime.FirstOrDefault(x => x.Type == type)?.Total ?? 0m;
        var ledgerNet =
            Sum(WalletTransactionType.Deposit) + Sum(WalletTransactionType.RefundCredit) + Sum(WalletTransactionType.AdminAdjustment)
            - Sum(WalletTransactionType.Spend) - Sum(WalletTransactionType.AdminDebit);

        var inPeriod = await _context.WalletTransactions.AsNoTracking()
            .Where(t => t.DateCreated >= fromUtc && t.DateCreated < toUtcExclusive)
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Count = g.Count(), Total = g.Sum(x => x.Amount) })
            .ToListAsync();

        WalletMovementRow Row(WalletTransactionType type, string label, bool isCredit)
        {
            var hit = inPeriod.FirstOrDefault(x => x.Type == type);
            return new WalletMovementRow { Label = label, IsCredit = isCredit, Count = hit?.Count ?? 0, Total = hit?.Total ?? 0m };
        }

        var cutoff = DateTime.UtcNow.AddDays(-DormantDays);
        var dormant = accounts.Where(w => w.Balance > 0 && !w.Transactions.Any(t => t.DateCreated >= cutoff));
        var dormantAmount = await dormant.SumAsync(w => (decimal?)w.Balance) ?? 0m;
        var dormantCount = await dormant.CountAsync();

        var top = await accounts
            .Where(w => w.Balance > 0)
            .OrderByDescending(w => w.Balance)
            .Take(10)
            .Select(w => new WalletAdminListItem
            {
                CustomerId = w.CustomerId,
                FullName = w.Customer != null ? w.Customer.FullName : "(removed user)",
                Email = w.Customer != null ? w.Customer.Email : null,
                Balance = w.Balance,
                LastActivity = w.Transactions.Max(t => (DateTime?)t.DateCreated)
            })
            .ToListAsync();

        return View(new WalletLiabilityViewModel
        {
            From = fromDate,
            To = toDate,
            TotalLiability = totalLiability,
            TotalWallets = totalWallets,
            WalletsWithBalance = withBalance,
            LedgerNet = ledgerNet,
            Movements = new List<WalletMovementRow>
            {
                Row(WalletTransactionType.Deposit, "Top-ups (Paystack)", true),
                Row(WalletTransactionType.RefundCredit, "Refunds to wallet", true),
                Row(WalletTransactionType.AdminAdjustment, "Staff credits", true),
                Row(WalletTransactionType.Spend, "Spent on orders", false),
                Row(WalletTransactionType.AdminDebit, "Staff debits", false)
            },
            DormantDays = DormantDays,
            DormantWallets = dormantCount,
            DormantAmount = dormantAmount,
            TopBalances = top
        });
    }

    // GET: /WalletAdmin/ExportBalances - every wallet holding money, as CSV for the accountant.
    [HttpGet]
    public async Task<IActionResult> ExportBalances()
    {
        var rows = await _context.WalletAccounts.AsNoTracking()
            .Where(w => w.Balance > 0)
            .OrderByDescending(w => w.Balance)
            .Select(w => new
            {
                Name = w.Customer != null ? w.Customer.FullName : "",
                Email = w.Customer != null ? w.Customer.Email : "",
                w.Balance,
                LastActivity = w.Transactions.Max(t => (DateTime?)t.DateCreated)
            })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Customer,Email,Balance (ZAR),Last activity (UTC)");
        foreach (var r in rows)
        {
            sb.Append(Csv(r.Name)).Append(',')
              .Append(Csv(r.Email)).Append(',')
              .Append(r.Balance.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.LastActivity?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "")
              .AppendLine();
        }

        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"fixcash-balances-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>Quotes a CSV cell and neutralises spreadsheet formulas (a name starting with = + - @ would
    /// otherwise be executed by Excel).</summary>
    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
