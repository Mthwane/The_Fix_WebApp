using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum PaymentIncidentStatus
{
    /// <summary>Everything that was taken (card, FixCash wallet, reward points) was returned automatically.</summary>
    AutoResolved = 0,
    /// <summary>Something could not be returned automatically - a staff member needs to act.</summary>
    NeedsAttention = 1,
    /// <summary>A staff member dealt with it and closed it.</summary>
    Resolved = 2
}

public enum PaymentIncidentSource
{
    SavedCardCheckout = 0,
    PaystackCheckout = 1,
    WalletCheckout = 2
}

/// <summary>
/// A checkout where money (card, FixCash wallet, or reward points) moved but the order could not be
/// created. One row per payment reference (unique index). PaymentRecoveryService returns what it can
/// automatically; PaymentIncidentsController is where staff handle the rest.
/// </summary>
public class PaymentIncident
{
    [Key]
    public int PaymentIncidentId { get; set; }

    /// <summary>The order/payment reference (WEB-...) - also the Paystack reference for card money.</summary>
    [Required, MaxLength(60)]
    public string Reference { get; set; } = string.Empty;

    public string? CustomerId { get; set; }
    public ApplicationUser? Customer { get; set; }

    public PaymentIncidentSource Source { get; set; }

    /// <summary>Money captured by the card gateway under this reference.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal CardAmount { get; set; }

    /// <summary>Money debited from the customer's FixCash wallet under this reference.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal WalletAmount { get; set; }

    public int PointsRedeemed { get; set; }

    public bool CardRefunded { get; set; }
    public bool WalletRestored { get; set; }
    public bool PointsRestored { get; set; }

    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public PaymentIncidentStatus Status { get; set; }

    [MaxLength(500)]
    public string? ResolutionNote { get; set; }

    public string? ResolvedByUserId { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? DateResolved { get; set; }
}
