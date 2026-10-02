using System.Net;
using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services;

/// <summary>
/// Customer-facing emails for order steps and FixCash movements. Every method is best-effort:
/// it never throws and never blocks the action that triggered it (IEmailSender already logs and
/// swallows delivery failures; the try/catch here also covers building the message).
/// Callers pass the customer explicitly so no method depends on an Include() having been done.
/// </summary>
public interface ICustomerNotificationService
{
    /// <summary>Order handed to the courier / marked shipped.</summary>
    Task OrderDispatchedAsync(ApplicationUser? customer, Order order, string? trackingReference = null);

    Task OrderDeliveredAsync(ApplicationUser? customer, Order order);

    /// <summary>Order cancelled, with what was returned to the customer and where.</summary>
    Task OrderCancelledAsync(ApplicationUser? customer, Order order, string? reason,
        decimal walletRefunded, decimal cardRefunded, bool cardRefundFailed);

    /// <summary>A return was processed, with the refund split between wallet and card.</summary>
    Task ReturnRefundAsync(ApplicationUser? customer, Order order, decimal toWallet, decimal toCard, bool cardRefundFailed);

    Task TopUpReceivedAsync(ApplicationUser? customer, decimal amount, decimal newBalance, string reference);

    /// <summary>Staff added or removed FixCash manually. signedAmount is positive for a credit.</summary>
    Task WalletAdjustedAsync(ApplicationUser? customer, decimal signedAmount, decimal newBalance, string reason);
}

public class CustomerNotificationService : ICustomerNotificationService
{
    private readonly IEmailSender _email;
    private readonly ILogger<CustomerNotificationService> _logger;

    public CustomerNotificationService(IEmailSender email, ILogger<CustomerNotificationService> logger)
    {
        _email = email;
        _logger = logger;
    }

    public Task OrderDispatchedAsync(ApplicationUser? customer, Order order, string? trackingReference = null) =>
        SendAsync(customer, $"Your order {order.OrderNumber} is on its way", () =>
        {
            var tracking = string.IsNullOrWhiteSpace(trackingReference)
                ? ""
                : $"<p>Tracking reference: <strong>{E(trackingReference)}</strong></p>";
            return $"<p>Good news - your order <strong>{E(order.OrderNumber)}</strong> has been dispatched.</p>" +
                   tracking +
                   "<p>You can follow its progress any time under My Orders.</p>";
        });

    public Task OrderDeliveredAsync(ApplicationUser? customer, Order order) =>
        SendAsync(customer, $"Your order {order.OrderNumber} has been delivered", () =>
            $"<p>Your order <strong>{E(order.OrderNumber)}</strong> has been delivered.</p>" +
            "<p>We hope you love it. If anything isn't right, reply to this email or open a support ticket and we'll help.</p>");

    public Task OrderCancelledAsync(ApplicationUser? customer, Order order, string? reason,
        decimal walletRefunded, decimal cardRefunded, bool cardRefundFailed) =>
        SendAsync(customer, $"Order {order.OrderNumber} cancelled", () =>
        {
            var reasonHtml = string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {E(reason)}.";
            var refundLines = RefundLines(walletRefunded, cardRefunded, cardRefundFailed);
            return $"<p>Your order <strong>{E(order.OrderNumber)}</strong> has been cancelled.{reasonHtml}</p>" +
                   (refundLines.Length > 0 ? refundLines : "");
        });

    public Task ReturnRefundAsync(ApplicationUser? customer, Order order, decimal toWallet, decimal toCard, bool cardRefundFailed) =>
        SendAsync(customer, $"Refund for your return - order {order.OrderNumber}", () =>
            $"<p>We've processed your return on order <strong>{E(order.OrderNumber)}</strong>.</p>" +
            RefundLines(toWallet, toCard, cardRefundFailed));

    public Task TopUpReceivedAsync(ApplicationUser? customer, decimal amount, decimal newBalance, string reference) =>
        SendAsync(customer, "FixCash top-up received", () =>
            $"<p>We've added <strong>{amount:C}</strong> to your FixCash wallet.</p>" +
            $"<p>New balance: <strong>{newBalance:C}</strong><br/>Payment reference: {E(reference)}</p>");

    public Task WalletAdjustedAsync(ApplicationUser? customer, decimal signedAmount, decimal newBalance, string reason) =>
        SendAsync(customer, "Your FixCash balance was updated", () =>
        {
            var verb = signedAmount >= 0 ? "added to" : "removed from";
            return $"<p><strong>{Math.Abs(signedAmount):C}</strong> was {verb} your FixCash wallet by our team.</p>" +
                   $"<p>Reason: {E(reason)}</p>" +
                   $"<p>New balance: <strong>{newBalance:C}</strong></p>";
        });

    // ---------- helpers ----------

    private static string RefundLines(decimal toWallet, decimal toCard, bool cardRefundFailed)
    {
        var lines = "";
        if (toWallet > 0)
            lines += $"<p><strong>{toWallet:C}</strong> has been returned to your FixCash wallet and is available to spend straight away.</p>";

        if (toCard > 0 && !cardRefundFailed)
            lines += $"<p><strong>{toCard:C}</strong> is being refunded to the card you paid with. Card refunds can take several business days to show on your statement, depending on your bank.</p>";
        else if (toCard > 0 && cardRefundFailed)
            lines += $"<p>Your card refund of <strong>{toCard:C}</strong> needs a manual check by our team. We'll be in touch - you don't need to do anything.</p>";

        return lines;
    }

    private async Task SendAsync(ApplicationUser? customer, string subject, Func<string> buildInner)
    {
        if (customer is null || string.IsNullOrWhiteSpace(customer.Email)) return;

        try
        {
            var html =
                $"<p>Hi {E(string.IsNullOrWhiteSpace(customer.FullName) ? "there" : customer.FullName)},</p>" +
                buildInner() +
                "<p>Thanks for shopping with Fashion Fix.</p>";

            await _email.SendAsync(customer.Email, subject, html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send '{Subject}' email.", subject);
        }
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
