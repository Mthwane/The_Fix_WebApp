using System.Globalization;
using System.Net;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Services;

public class PaymentRecoveryService : IPaymentRecoveryService
{
    private static readonly CultureInfo Za = new("en-ZA");

    private readonly ApplicationDbContext _context;
    private readonly IWalletService _wallet;
    private readonly IRewardsService _rewards;
    private readonly IPaymentService _payments;
    private readonly IEmailSender _email;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PaymentRecoveryService> _logger;

    public PaymentRecoveryService(
        ApplicationDbContext context,
        IWalletService wallet,
        IRewardsService rewards,
        IPaymentService payments,
        IEmailSender email,
        UserManager<ApplicationUser> userManager,
        ILogger<PaymentRecoveryService> logger)
    {
        _context = context;
        _wallet = wallet;
        _rewards = rewards;
        _payments = payments;
        _email = email;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<PaymentRecoveryOutcome> HandleFailedOrderAsync(PaymentFailureContext c)
    {
        _logger.LogError("Order creation failed after payment {Reference}: {Reason}", c.Reference, c.Reason);

        // One incident per reference - a repeated call (double-click, retry) just reports the existing one.
        var existing = await _context.PaymentIncidents.AsNoTracking().FirstOrDefaultAsync(i => i.Reference == c.Reference);
        if (existing is not null) return Outcome(existing, c);

        var incident = new PaymentIncident
        {
            Reference = c.Reference,
            CustomerId = c.CustomerId,
            Source = c.Source,
            CardAmount = c.CardCharged,
            WalletAmount = c.WalletDebited,
            PointsRedeemed = c.PointsRedeemed,
            Reason = Truncate(c.Reason, 480)
        };

        // 1) FixCash wallet - reversible instantly and safely.
        incident.WalletRestored = c.WalletDebited <= 0;
        if (c.WalletDebited > 0)
        {
            try { incident.WalletRestored = (await _wallet.RestoreDebitAsync(c.CustomerId, c.Reference)).Success; }
            catch (Exception ex) { _logger.LogError(ex, "Restoring FixCash for {Reference} failed.", c.Reference); }
        }

        // 2) Reward points.
        incident.PointsRestored = c.PointsRedeemed <= 0;
        if (c.PointsRedeemed > 0)
        {
            try { incident.PointsRestored = (await _rewards.RestoreRedemptionAsync(c.CustomerId, c.Reference)).Success; }
            catch (Exception ex) { _logger.LogError(ex, "Restoring points for {Reference} failed.", c.Reference); }
        }

        // 3) The card - refunded through the gateway unless something looked suspicious.
        incident.CardRefunded = c.CardCharged <= 0;
        if (c.CardCharged > 0 && c.AutoRefundCard)
        {
            try
            {
                var refund = await _payments.RefundTransactionAsync(c.Reference, c.CardCharged);
                incident.CardRefunded = refund.Success;
                if (!refund.Success)
                    incident.Reason = Truncate($"{incident.Reason} | Card refund failed: {refund.ErrorMessage}", 500);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Card refund for {Reference} threw.", c.Reference);
                incident.Reason = Truncate($"{incident.Reason} | Card refund error: {ex.Message}", 500);
            }
        }

        var allReturned = incident.WalletRestored && incident.PointsRestored && incident.CardRefunded;
        incident.Status = allReturned ? PaymentIncidentStatus.AutoResolved : PaymentIncidentStatus.NeedsAttention;
        if (allReturned)
        {
            incident.DateResolved = DateTime.UtcNow;
            incident.ResolutionNote = "Returned automatically.";
        }

        try
        {
            _context.PaymentIncidents.Add(incident);
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = c.CustomerId,
                Action = allReturned ? "PaymentIncidentAutoResolved" : "PaymentIncidentNeedsAttention",
                Details = Truncate($"Order not created for {c.Reference}. Card {c.CardCharged:0.00} (refunded: {incident.CardRefunded}), " +
                                   $"FixCash {c.WalletDebited:0.00} (restored: {incident.WalletRestored}), points {c.PointsRedeemed} (restored: {incident.PointsRestored}).", 490)
            });
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Lost a race with a parallel call for the same reference (unique index) - report the winner's row.
            _logger.LogWarning(ex, "Payment incident for {Reference} was already recorded.", c.Reference);
            foreach (var e in _context.ChangeTracker.Entries().Where(e => e.Entity is PaymentIncident or AuditLog).ToList())
                e.State = EntityState.Detached;
            var winner = await _context.PaymentIncidents.AsNoTracking().FirstOrDefaultAsync(i => i.Reference == c.Reference);
            if (winner is not null) return Outcome(winner, c);
        }

        await NotifyCustomerAsync(c, incident);
        if (!allReturned) await NotifyStaffAsync(c, incident);

        return Outcome(incident, c);
    }

    // ---------- Messages ----------

    private static PaymentRecoveryOutcome Outcome(PaymentIncident i, PaymentFailureContext c)
    {
        var returned = i.Status != PaymentIncidentStatus.NeedsAttention;
        return new PaymentRecoveryOutcome
        {
            Incident = i,
            EverythingReturned = returned,
            CustomerMessage = BuildCustomerMessage(i, returned)
        };
    }

    private static string BuildCustomerMessage(PaymentIncident i, bool returned)
    {
        if (!returned)
            return $"Your payment went through but we couldn't complete your order. Our team has been alerted and will refund you - please quote reference {i.Reference} if you contact us.";

        var parts = new List<string>();
        if (i.WalletAmount > 0) parts.Add($"{Money(i.WalletAmount)} was returned to your FixCash wallet");
        if (i.PointsRedeemed > 0) parts.Add($"{i.PointsRedeemed:N0} points were returned");
        if (i.CardAmount > 0) parts.Add($"your card payment of {Money(i.CardAmount)} is being refunded (it can take a few business days to show)");

        var joined = string.Join("; ", parts);
        var detail = joined.Length == 0 ? "" : " " + char.ToUpper(joined[0]) + joined[1..] + ".";
        return "We couldn't complete your order, so nothing has been kept." + detail + " Your cart is still saved - please try again.";
    }

    private static string Money(decimal v) => v.ToString("C", Za);
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // ---------- Emails (best-effort) ----------

    private async Task NotifyCustomerAsync(PaymentFailureContext c, PaymentIncident i)
    {
        try
        {
            var customer = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == c.CustomerId);
            var to = customer?.Email ?? c.CustomerEmail;
            if (string.IsNullOrWhiteSpace(to)) return;

            var name = WebUtility.HtmlEncode(customer?.FullName ?? c.CustomerName ?? "there");
            var message = WebUtility.HtmlEncode(BuildCustomerMessage(i, i.Status != PaymentIncidentStatus.NeedsAttention));

            await _email.SendAsync(to, "We couldn't complete your order",
                $"<p>Hi {name},</p><p>{message}</p><p>Reference: <strong>{WebUtility.HtmlEncode(i.Reference)}</strong></p>" +
                "<p>We're sorry about that.</p>");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not email the customer about payment incident {Reference}.", c.Reference);
        }
    }

    private async Task NotifyStaffAsync(PaymentFailureContext c, PaymentIncident i)
    {
        try
        {
            var recipients = new List<ApplicationUser>();
            foreach (var role in new[] { "Administrator", "Manager" })
                recipients.AddRange((await _userManager.GetUsersInRoleAsync(role)).Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Email)));

            var body = $@"<h2>Payment needs attention</h2>
                <p>An order could not be created after payment <strong>{WebUtility.HtmlEncode(i.Reference)}</strong>.</p>
                <ul>
                  <li>Card: {Money(i.CardAmount)} (refunded: {(i.CardRefunded ? "yes" : "NO")})</li>
                  <li>FixCash: {Money(i.WalletAmount)} (restored: {(i.WalletRestored ? "yes" : "NO")})</li>
                  <li>Points: {i.PointsRedeemed:N0} (restored: {(i.PointsRestored ? "yes" : "NO")})</li>
                </ul>
                <p>Reason: {WebUtility.HtmlEncode(i.Reason)}</p>
                <p>Open <strong>Payment Incidents</strong> in the staff area to finish it.</p>";

            foreach (var staff in recipients.DistinctBy(u => u.Id))
                await _email.SendAsync(staff.Email!, $"Payment needs attention - {i.Reference}", body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not email staff about payment incident {Reference}.", c.Reference);
        }
    }
}
