using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services;

public class PaymentFailureContext
{
    public string Reference { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
    public PaymentIncidentSource Source { get; set; }

    /// <summary>Amount the card gateway actually captured under this reference (0 if none).</summary>
    public decimal CardCharged { get; set; }

    /// <summary>Amount actually debited from the FixCash wallet under this reference (0 if none).</summary>
    public decimal WalletDebited { get; set; }

    /// <summary>Reward points actually redeemed under this reference (0 if none).</summary>
    public int PointsRedeemed { get; set; }

    /// <summary>Set false when something looks wrong (e.g. the amount paid doesn't match the cart): the card
    /// is then left for a person to review instead of being refunded automatically.</summary>
    public bool AutoRefundCard { get; set; } = true;

    public string Reason { get; set; } = string.Empty;
}

public class PaymentRecoveryOutcome
{
    public PaymentIncident Incident { get; set; } = null!;
    public bool EverythingReturned { get; set; }

    /// <summary>Plain-language explanation that is safe to show the customer.</summary>
    public string CustomerMessage { get; set; } = string.Empty;
}

/// <summary>
/// The safety net for "money moved, but we couldn't create the order". Hands back whatever can be
/// handed back automatically (FixCash wallet, reward points, the card via a gateway refund), writes a
/// durable PaymentIncident row for anything that can't, and tells the customer and staff.
/// </summary>
public interface IPaymentRecoveryService
{
    Task<PaymentRecoveryOutcome> HandleFailedOrderAsync(PaymentFailureContext context);
}
