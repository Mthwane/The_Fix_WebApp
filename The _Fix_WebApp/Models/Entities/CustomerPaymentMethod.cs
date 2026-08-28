using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// A saved card, so a returning customer can check out without re-entering their card
/// details every time.
///
/// IMPORTANT - PCI compliance: this table NEVER stores a card number, CVV, or expiry
/// beyond the display-only month/year. What's actually stored is Paystack's
/// "authorization_code" - an opaque token Paystack hands back after a successful payment
/// that this app can later replay via their "charge authorization" endpoint to bill the
/// same card again. The real card data lives only on Paystack's PCI-compliant servers,
/// never on ours. See PaystackPaymentService.ChargeAuthorizationAsync.
/// </summary>
public class CustomerPaymentMethod
{
    [Key]
    public int CustomerPaymentMethodId { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser Customer { get; set; } = null!;

    /// <summary>Paystack's reusable charge token - the only thing that actually lets us bill this card again.</summary>
    [Required, MaxLength(100)]
    public string AuthorizationCode { get; set; } = string.Empty;

    /// <summary>Last 4 digits only, for display ("Visa ending in 4242") - never the full number.</summary>
    [MaxLength(4)]
    public string? Last4 { get; set; }

    [MaxLength(20)]
    public string? CardType { get; set; } // e.g. "visa", "mastercard"

    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }

    [MaxLength(100)]
    public string? Bank { get; set; }

    /// <summary>The card pre-selected at checkout. Only one per customer - enforced in CustomerController.</summary>
    public bool IsDefault { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}