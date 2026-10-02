using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Controllers;

/// <summary>Customer-facing FixCash wallet. Deposits go through Paystack, mirroring
/// PaymentsController's checkout callback almost exactly (verify -> reconcile amount -> commit) -
/// but crediting a wallet instead of creating an Order, so it's its own controller/callback
/// rather than overloading that one.</summary>
[Authorize(Roles = "Customer")]
public class WalletController : Controller
{
    private readonly IWalletService _wallet;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<WalletController> _logger;
    private readonly ICustomerNotificationService _notify;

    private const int HistoryPageSize = 15;

    public WalletController(
        IWalletService wallet,
        UserManager<ApplicationUser> userManager,
        ILogger<WalletController> logger,
        ICustomerNotificationService notify)
    {
        _wallet = wallet;
        _userManager = userManager;
        _logger = logger;
        _notify = notify;
    }

    // GET: /Wallet?page=2
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = _userManager.GetUserId(User)!;
        page = Math.Max(1, page);

        var (items, total) = await _wallet.GetHistoryPageAsync(userId, page, HistoryPageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)HistoryPageSize));

        // A page past the end (e.g. a stale bookmark) falls back to the last real page.
        if (page > totalPages)
        {
            page = totalPages;
            (items, total) = await _wallet.GetHistoryPageAsync(userId, page, HistoryPageSize);
        }

        return View(new WalletIndexViewModel
        {
            Balance = await _wallet.GetBalanceAsync(userId),
            Items = items,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total
        });
    }

    // GET: /Wallet/Deposit
    [HttpGet]
    public IActionResult Deposit() => View();

    // POST: /Wallet/Deposit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deposit(decimal amount, [FromServices] IPaymentService payments)
    {
        if (amount < 10)
        {
            this.ToastError("Minimum top-up is R10.00.");
            return View();
        }
        if (amount > 50000)
        {
            this.ToastError("Maximum top-up is R50,000.00 per deposit.");
            return View();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            this.ToastError("Your account needs a valid email address before you can deposit online.");
            return View();
        }

        var reference = $"FIXCASH-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var callbackUrl = Url.Action(nameof(DepositCallback), "Wallet", null, Request.Scheme)!;
        var initResult = await payments.InitializeTransactionAsync(user.Email, amount, reference, callbackUrl);

        if (!initResult.Success)
        {
            this.ToastError($"Could not start payment: {initResult.ErrorMessage}");
            return View();
        }

        HttpContext.Session.SetString("PendingWalletDepositReference", reference);
        HttpContext.Session.SetString("PendingWalletDepositAmount", amount.ToString("F2"));

        return Redirect(initResult.AuthorizationUrl!);
    }

    // GET: /Wallet/DepositCallback?reference=FIXCASH-xxxx&trxref=FIXCASH-xxxx
    [HttpGet]
    public async Task<IActionResult> DepositCallback(string? reference, string? trxref, [FromServices] IPaymentService payments)
    {
        var actualReference = reference ?? trxref;
        var pendingReference = HttpContext.Session.GetString("PendingWalletDepositReference");
        var pendingAmountRaw = HttpContext.Session.GetString("PendingWalletDepositAmount");

        if (string.IsNullOrEmpty(actualReference) || actualReference != pendingReference)
        {
            this.ToastError("This payment session doesn't match your deposit - please try again.");
            return RedirectToAction(nameof(Index));
        }

        var verifyResult = await payments.VerifyTransactionAsync(actualReference);
        if (!verifyResult.Success)
        {
            this.ToastError($"Deposit was not completed: {verifyResult.ErrorMessage}");
            return RedirectToAction(nameof(Index));
        }

        // Same belt-and-braces reconciliation PaymentsController.Callback does for a checkout -
        // what Paystack actually confirms must match what this deposit attempt says it's for.
        if (decimal.TryParse(pendingAmountRaw, out var pendingAmount) && Math.Abs(pendingAmount - verifyResult.AmountRands) > 0.01m)
        {
            _logger.LogError(
                "Wallet deposit {Reference} verified for {Paid:C} but the pending amount was {Expected:C} - refusing to credit automatically.",
                actualReference, verifyResult.AmountRands, pendingAmount);
            this.ToastError($"Your payment succeeded but the amount doesn't match - nothing has been charged incorrectly, but we need to check this manually. Please contact support with reference {actualReference}.");
            return RedirectToAction(nameof(Index));
        }

        var userId = _userManager.GetUserId(User)!;
        var creditResult = await _wallet.CreditFromDepositAsync(userId, verifyResult.AmountRands, actualReference);

        HttpContext.Session.Remove("PendingWalletDepositReference");
        HttpContext.Session.Remove("PendingWalletDepositAmount");

        if (!creditResult.Success)
        {
            _logger.LogError("Wallet deposit {Reference} verified by Paystack but crediting failed: {Error}", actualReference, creditResult.ErrorMessage);
            this.ToastError($"Your payment succeeded but we couldn't credit your wallet automatically. Please contact support with reference {actualReference}.");
            return RedirectToAction(nameof(Index));
        }

        this.ToastSuccess(creditResult.AlreadyProcessed
            ? "This deposit was already credited."
            : $"R{verifyResult.AmountRands:N2} added to your FixCash balance.");

        // Only email for a genuinely new credit - a duplicate callback must not send a second receipt.
        if (!creditResult.AlreadyProcessed)
        {
            var customer = await _userManager.GetUserAsync(User);
            await _notify.TopUpReceivedAsync(customer, verifyResult.AmountRands, creditResult.BalanceAfter, actualReference);
        }

        return RedirectToAction(nameof(Index));
    }
}
