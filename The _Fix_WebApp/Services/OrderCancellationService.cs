using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Services;

/// <summary>What a cancellation did. <see cref="Message"/> is ready to show in a toast.</summary>
public sealed record OrderCancellationResult(
    bool NotFound,
    bool Cancelled,
    string Message,
    bool HasRefundProblem = false,
    decimal WalletRefunded = 0m,
    decimal CardRefunded = 0m);

/// <summary>
/// The single place a STAFF-side order cancellation happens (Orders screen and the support-ticket quick action both
/// call it, so they can never drift apart). A customer cancelling their own order still goes through
/// CustomerController.CancelOrder.
///
/// What it does, in order: marks the order Cancelled, puts every item back into stock, reverses the reward points the
/// order earned, then returns the customer's money to the ORIGINAL payment - the FixCash part back into the wallet and the
/// card part refunded through Paystack against the order's own payment reference. Because GrandTotal includes the
/// delivery fee, the delivery fee is refunded too. Each refund leg is attempted independently; a failed leg is reported
/// (and audited) so staff can finish it by hand, but never undoes the cancellation. In-store (POS) sales are settled at
/// the till, so no automatic refund is attempted for them.
/// </summary>
public interface IOrderCancellationService
{
    Task<OrderCancellationResult> CancelAsync(int orderId, string? actingUserId, string? reason, string? source = null);
}

public class OrderCancellationService : IOrderCancellationService
{
    /// <summary>Statuses an order can still be cancelled from - not once Delivered/Completed.</summary>
    private static readonly OrderStatus[] CancellableStatuses =
    {
        OrderStatus.Pending,
        OrderStatus.Processing,
        OrderStatus.Shipped
    };

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventory;
    private readonly IRewardsService _rewards;
    private readonly IWalletService _wallet;
    private readonly IPaymentService _payments;
    private readonly ICustomerNotificationService _notify;
    private readonly ILogger<OrderCancellationService> _logger;

    public OrderCancellationService(
        ApplicationDbContext context,
        IInventoryService inventory,
        IRewardsService rewards,
        IWalletService wallet,
        IPaymentService payments,
        ICustomerNotificationService notify,
        ILogger<OrderCancellationService> logger)
    {
        _context = context;
        _inventory = inventory;
        _rewards = rewards;
        _wallet = wallet;
        _payments = payments;
        _notify = notify;
        _logger = logger;
    }

    public async Task<OrderCancellationResult> CancelAsync(int orderId, string? actingUserId, string? reason, string? source = null)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

        if (order is null)
            return new OrderCancellationResult(true, false, "That order could not be found.");

        if (!CancellableStatuses.Contains(order.Status))
            return new OrderCancellationResult(false, false,
                $"Order {order.OrderNumber} is {order.Status} and can no longer be cancelled.");

        try
        {
            order.Status = OrderStatus.Cancelled;

            // Items from before the variant rework have no ProductVariantId - they can't be restocked automatically.
            var restockLines = order.OrderItems
                .Where(i => i.ProductVariantId.HasValue)
                .Select(i => (i.ProductVariantId!.Value, i.Quantity))
                .ToList();

            var unrestockable = order.OrderItems.Count(i => !i.ProductVariantId.HasValue);
            if (unrestockable > 0)
                _logger.LogWarning("Order {OrderNumber} cancelled with {Count} pre-variant line item(s) that could not be auto-restocked.",
                    order.OrderNumber, unrestockable);

            if (restockLines.Count > 0)
                await _inventory.IncrementStockBatchAsync(restockLines, InventoryChangeReason.OrderCancelled);

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = actingUserId,
                Action = "OrderCancelled",
                Details = Trim($"Cancelled order {order.OrderNumber}" + (string.IsNullOrWhiteSpace(source) ? "." : $" ({source}).") +
                               (string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}"))
            });

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order {OrderId}.", orderId);
            return new OrderCancellationResult(false, false, "Something went wrong cancelling this order - please try again.");
        }

        // The cancellation is committed. Everything below is best-effort and can only be reported, never undone.
        try
        {
            await _rewards.ReverseForCancelledOrderAsync(order.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order {OrderNumber} cancelled but reversing its reward points failed.", order.OrderNumber);
        }

        var refund = await RefundAsync(order, actingUserId);

        var message = $"Order {order.OrderNumber} was cancelled and stock restored.";
        if (refund.Wallet > 0)
            message += $" {refund.Wallet:C} returned to the customer's FixCash wallet.";
        if (refund.Card > 0 && !refund.CardFailed)
            message += $" {refund.Card:C} refunded to the original card payment.";

        var hasProblem = refund.WalletError is not null || refund.CardFailed;
        if (hasProblem)
        {
            var problems = new List<string>();
            if (refund.WalletError is not null) problems.Add($"FixCash refund failed ({refund.WalletError})");
            if (refund.CardFailed) problems.Add($"{refund.Card:C} card refund failed ({refund.CardError ?? "gateway error"})");
            message += $" BUT: {string.Join("; ", problems)}. Refund the customer manually.";
        }

        try
        {
            await _notify.OrderCancelledAsync(order.Customer, order, reason, refund.Wallet, refund.Card, refund.CardFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order {OrderNumber} cancelled but the customer email failed.", order.OrderNumber);
        }

        return new OrderCancellationResult(false, true, message, hasProblem, refund.Wallet, refund.CardFailed ? 0m : refund.Card);
    }

    private sealed record RefundOutcome(decimal Wallet, decimal Card, bool CardFailed, string? CardError, string? WalletError);

    /// <summary>Online orders only. FixCash portion -> wallet (idempotent per order); card portion -> Paystack refund against
    /// the order's payment reference (an online order's OrderNumber IS its Paystack reference).</summary>
    private async Task<RefundOutcome> RefundAsync(Order order, string? actingUserId)
    {
        if (order.OrderType != OrderType.Online || string.IsNullOrEmpty(order.CustomerId))
            return new RefundOutcome(0m, 0m, false, null, null);

        decimal wallet = 0m;
        string? walletError = null;

        if (order.WalletAmountApplied > 0)
        {
            try
            {
                var result = await _wallet.RefundCancelledOrderAsync(order);
                if (result.Success) wallet = order.WalletAmountApplied;
                else walletError = result.ErrorMessage ?? "unknown error";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FixCash refund for cancelled order {OrderNumber} threw.", order.OrderNumber);
                walletError = "unexpected error";
            }
        }

        decimal card = 0m;
        var cardFailed = false;
        string? cardError = null;
        var cardPart = Math.Round(order.GrandTotal - order.WalletAmountApplied, 2, MidpointRounding.AwayFromZero);
        var paidByCard = order.PaymentMethod is PaymentMethod.CreditCard or PaymentMethod.DebitCard;

        if (cardPart > 0 && paidByCard)
        {
            card = cardPart;
            try
            {
                var result = await _payments.RefundTransactionAsync(order.OrderNumber, cardPart);
                if (!result.Success)
                {
                    cardFailed = true;
                    cardError = result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Card refund for cancelled order {OrderNumber} threw.", order.OrderNumber);
                cardFailed = true;
                cardError = "unexpected error";
            }
        }

        if (wallet > 0 || card > 0 || walletError is not null)
        {
            var details =
                $"Refund for cancelled order {order.OrderNumber}: " +
                $"FixCash {wallet:0.00}" + (walletError is null ? "" : $" (FAILED: {walletError})") +
                $", card {card:0.00}" + (card > 0 ? (cardFailed ? $" (FAILED: {cardError})" : " (requested)") : "") + ".";

            try
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = actingUserId,
                    Action = (walletError is not null || cardFailed) ? "CancelRefundFailed" : "CancelRefundIssued",
                    Details = Trim(details)
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not write the refund audit entry for order {OrderNumber}.", order.OrderNumber);
            }
        }

        return new RefundOutcome(wallet, card, cardFailed, cardError, walletError);
    }

    private static string Trim(string s) => s.Length <= 500 ? s : s[..500];
}
