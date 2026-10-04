using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Over-the-counter returns. Ported from V1 with one substantive change: a resalable return now
/// restocks the exact ProductVariant that came back, not the parent style.
/// </summary>
[Authorize(Policy = Permissions.ReturnsProcess)]
public class ReturnsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IInventoryService _inventoryService;
    private readonly IWalletService _walletService;
    private readonly IRewardsService _rewardsService;
    private readonly IPaymentService _payments;
    private readonly ICustomerNotificationService _notify;
    private readonly ILogger<ReturnsController> _logger;

    public ReturnsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IInventoryService inventoryService,
        IWalletService walletService,
        IRewardsService rewardsService,
        IPaymentService payments,
        ICustomerNotificationService notify,
        ILogger<ReturnsController> logger)
    {
        _context = context;
        _userManager = userManager;
        _inventoryService = inventoryService;
        _walletService = walletService;
        _rewardsService = rewardsService;
        _payments = payments;
        _notify = notify;
        _logger = logger;
    }

    private const int ReturnsPageSize = 10;

    // GET: /Returns?filter=all|completed|inprogress&page=2 - newest first, 10 per page.
    // Completed = refund fully back with the customer. In progress = a refund leg failed or still needs a manual
    // follow-up (see ReturnStatus). The tab counts always reflect the whole table, not just the current filter.
    [HttpGet]
    public async Task<IActionResult> Index(string? filter, int? page)
    {
        var f = (filter ?? "all").Trim().ToLowerInvariant();
        if (f != "completed" && f != "inprogress") f = "all";

        var all = _context.ReturnTransactions.AsNoTracking();
        var query = f switch
        {
            "completed" => all.Where(r => r.Status == ReturnStatus.Completed),
            "inprogress" => all.Where(r => r.Status == ReturnStatus.InProgress),
            _ => all
        };

        var totalItems = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)ReturnsPageSize));
        var currentPage = PagerModel.ClampPage(page, totalPages);

        var returns = await query
            .Include(r => r.Order)
            .Include(r => r.OrderItem).ThenInclude(i => i.Product)
            .Include(r => r.ProductVariant)
            .Include(r => r.ProcessedByUser)
            .OrderByDescending(r => r.DateProcessed)
            .Skip((currentPage - 1) * ReturnsPageSize)
            .Take(ReturnsPageSize)
            .ToListAsync();

        ViewBag.Filter = f;
        ViewBag.AllCount = await all.CountAsync();
        ViewBag.CompletedCount = await all.CountAsync(r => r.Status == ReturnStatus.Completed);
        ViewBag.InProgressCount = await all.CountAsync(r => r.Status == ReturnStatus.InProgress);
        ViewBag.Pager = new PagerModel
        {
            Page = currentPage,
            TotalPages = totalPages,
            TotalItems = totalItems,
            PageSize = ReturnsPageSize,
            Action = nameof(Index),
            RouteValues = new Dictionary<string, string?> { ["filter"] = f }
        };

        return View(returns);
    }

    // POST: /Returns/MarkCompleted/5 - staff confirm the customer has now actually received their money
    // (for a return left In progress because a refund leg failed and was settled by hand).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCompleted(int id, string? filter, int? page)
    {
        var txn = await _context.ReturnTransactions.Include(r => r.Order).FirstOrDefaultAsync(r => r.ReturnId == id);
        if (txn is null) return NotFound();

        if (txn.Status != ReturnStatus.Completed)
        {
            txn.Status = ReturnStatus.Completed;
            txn.DateCompleted = DateTime.UtcNow;
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = _userManager.GetUserId(User),
                Action = "ReturnMarkedCompleted",
                Details = $"Return {id} on {txn.Order?.OrderNumber} marked completed (refund settled)."
            });
            await _context.SaveChangesAsync();
        }

        this.ToastSuccess("Return marked as completed.");
        return RedirectToAction(nameof(Index), new { filter, page });
    }

    // GET: /Returns/Lookup?orderNumber=WEB-123 - find the order to return against.
    [HttpGet]
    public async Task<IActionResult> Lookup(string? orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return View(null);

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OrderItems).ThenInclude(i => i.Product)
            .Include(o => o.OrderItems).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order is null)
        {
            this.ToastError($"No order found with number '{orderNumber}'.");
            return View(null);
        }

        // Show what's already been returned per line, so staff can't over-refund a line by
        // processing the same return twice.
        ViewBag.AlreadyReturned = await _context.ReturnTransactions
            .Where(r => r.OrderId == order.OrderId)
            .GroupBy(r => r.OrderItemId)
            .Select(g => new
            {
                OrderItemId = g.Key,
                Quantity = g.Sum(x => x.QuantityReturned)
            })
            .ToDictionaryAsync(x => x.OrderItemId, x => x.Quantity);

        ViewBag.OrderNumber = orderNumber;
        return View(order);
    }

    // POST: /Returns/Process
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(
        int orderItemId,
        int quantity,
        bool isResalable,
        RefundMethod refundMethod,
        string? reason)
    {
        var item = await _context.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product)
            .Include(i => i.ProductVariant)
            .FirstOrDefaultAsync(i => i.OrderItemId == orderItemId);

        if (item is null)
            return NotFound();

        if (quantity <= 0)
        {
            this.ToastError("Enter a quantity greater than zero.");
            return RedirectToAction(
                nameof(Lookup),
                new { orderNumber = item.Order.OrderNumber });
        }

        var alreadyReturned = await _context.ReturnTransactions
            .Where(r => r.OrderItemId == orderItemId)
            .SumAsync(r => (int?)r.QuantityReturned) ?? 0;

        var returnable = item.Quantity - alreadyReturned;

        if (quantity > returnable)
        {
            this.ToastError(
                $"Only {returnable} unit(s) left to return on that line ({alreadyReturned} already returned).");

            return RedirectToAction(
                nameof(Lookup),
                new { orderNumber = item.Order.OrderNumber });
        }

        // Refund what was actually paid for this line.
        // Orders carrying a discount (points redeemed or a POS discount) must not be
        // refunded at sticker price, or the discount would effectively become a cash payout.
        var discountShare = item.Order.SubTotal > 0
            ? item.Order.DiscountTotal / item.Order.SubTotal
            : 0m;

        var refundAmount = Math.Round(
            item.UnitPrice * quantity * (1m - discountShare),
            2,
            MidpointRounding.AwayFromZero);

        if (refundMethod == RefundMethod.StoreCredit &&
            string.IsNullOrEmpty(item.Order.CustomerId))
        {
            this.ToastError(
                "This order has no customer account attached, so there's no FixCash wallet to credit - use Original Payment instead.");

            return RedirectToAction(
                nameof(Lookup),
                new { orderNumber = item.Order.OrderNumber });
        }

        // Create the return transaction first so it receives its ReturnId.
        var returnTxn = new ReturnTransaction
        {
            OrderId = item.OrderId,
            OrderItemId = item.OrderItemId,
            ProductVariantId = item.ProductVariantId,
            ProcessedByUserId = _userManager.GetUserId(User)!,
            QuantityReturned = quantity,
            IsResalable = isResalable,
            RefundMethod = refundMethod,
            RefundAmount = refundAmount,
            Reason = reason
        };

        _context.ReturnTransactions.Add(returnTxn);

        // Only resalable stock goes back on the shelf. Damaged goods are still refunded but
        // written off - incrementing stock for them would silently inflate inventory.
        if (isResalable && item.ProductVariantId.HasValue)
        {
            await _inventoryService.IncrementStockAsync(
                item.ProductVariantId.Value,
                quantity,
                InventoryChangeReason.Return);
        }
        else if (isResalable)
        {
            // Pre-variant historical line: refund stands, but there's no variant to restock into.
            this.ToastWarning(
                "Refund processed, but this is a legacy order line with no size/colour recorded - restock it manually.");
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "ReturnProcessed",
            Details =
                $"Returned {quantity}x {item.Product?.Name} on {item.Order.OrderNumber}. " +
                $"Refund {refundAmount:C} via {refundMethod}. Resalable: {isResalable}."
        });

        // Save the return so ReturnId is available for the rewards reversal reference.
        await _context.SaveChangesAsync();

        // Reverse the reward points that were earned from the original order.
        var rewardsNote = "";

        if (!string.IsNullOrEmpty(item.Order.CustomerId))
        {
            try
            {
                var reversal = await _rewardsService.ReverseEarnedForReturnAsync(
                    item.OrderId,
                    $"RETURN-{returnTxn.ReturnId}");

                if (reversal.Points > 0)
                {
                    rewardsNote = $" {reversal.Points} reward point(s) reversed.";
                }
            }
            catch
            {
                rewardsNote =
                    " Reward points could not be reversed automatically - adjust manually.";
            }
        }

        // ---- Where the money goes ----
        // Store Credit: the whole refund goes to the customer's FixCash wallet.
        // Original Payment on an ONLINE order: the FixCash share goes back to the wallet and the card
        // share is requested from Paystack (the order number is the payment reference), split pro rata
        // to how the order was paid. Original Payment on a POS sale stays a manual at-the-till refund.
        var order = item.Order;
        decimal toWallet = 0m;
        decimal toCard = 0m;

        if (refundMethod == RefundMethod.StoreCredit)
        {
            toWallet = refundAmount;
        }
        else if (order.OrderType == OrderType.Online &&
                 !string.IsNullOrEmpty(order.CustomerId) &&
                 order.GrandTotal > 0)
        {
            if (order.WalletAmountApplied > 0)
            {
                toWallet = Math.Min(
                    refundAmount,
                    Math.Round(refundAmount * order.WalletAmountApplied / order.GrandTotal, 2, MidpointRounding.AwayFromZero));
            }

            if (order.PaymentMethod is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
                toCard = refundAmount - toWallet;
        }

        decimal walletCredited = 0m;
        var problems = new List<string>();
        var cardFailed = false;

        if (toWallet > 0)
        {
            var creditResult = await _walletService.CreditReturnAsync(
                order.CustomerId!,
                toWallet,
                item.OrderId,
                returnTxn.ReturnId,
                $"Refund for return on order {order.OrderNumber}.");

            if (creditResult.Success)
                walletCredited = toWallet;
            else
                problems.Add($"crediting the FixCash wallet failed ({creditResult.ErrorMessage}) - credit {toWallet:C} manually");
        }

        if (toCard > 0)
        {
            try
            {
                var cardResult = await _payments.RefundTransactionAsync(order.OrderNumber, toCard);
                if (!cardResult.Success)
                {
                    cardFailed = true;
                    problems.Add($"the {toCard:C} card refund was rejected ({cardResult.ErrorMessage}) - refund it manually");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Card refund for return {ReturnId} on order {OrderNumber} threw.", returnTxn.ReturnId, order.OrderNumber);
                cardFailed = true;
                problems.Add($"the {toCard:C} card refund failed - refund it manually");
            }
        }

        // A refund leg that failed leaves the return In progress until staff settle it by hand and mark it completed.
        if (problems.Count > 0)
        {
            returnTxn.Status = ReturnStatus.InProgress;
        }
        else
        {
            returnTxn.Status = ReturnStatus.Completed;
            returnTxn.DateCompleted = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();

        if (toWallet > 0 || toCard > 0)
        {
            var auditDetails =
                $"Return {returnTxn.ReturnId} on {order.OrderNumber}: FixCash {walletCredited:0.00} of {toWallet:0.00}, " +
                $"card {(cardFailed ? 0m : toCard):0.00} of {toCard:0.00}." +
                (problems.Count > 0 ? " PROBLEM: " + string.Join("; ", problems) : "");

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = _userManager.GetUserId(User),
                Action = problems.Count > 0 ? "ReturnRefundFailed" : "ReturnRefundIssued",
                Details = auditDetails.Length <= 500 ? auditDetails : auditDetails[..500]
            });
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(order.CustomerId))
            {
                var customer = await _userManager.FindByIdAsync(order.CustomerId);
                await _notify.ReturnRefundAsync(customer, order, walletCredited, toCard, cardFailed);
            }
        }

        if (problems.Count > 0)
        {
            this.ToastError(
                "Return processed and stock adjusted, but " + string.Join("; and ", problems) + ".");
        }
        else
        {
            var where = toWallet > 0 && toCard > 0
                ? $"{toWallet:C} to FixCash and {toCard:C} to the card"
                : toWallet > 0
                    ? "to the customer's FixCash wallet"
                    : toCard > 0
                        ? "to the card"
                        : $"via {refundMethod}";

            this.ToastSuccess(
                $"Return processed - {refundAmount:C} refunded ({where}).{rewardsNote}");
        }

        return RedirectToAction(
            nameof(Lookup),
            new { orderNumber = item.Order.OrderNumber });
    }
}
