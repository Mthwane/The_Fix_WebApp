using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum RewardEarnMode
{
    /// <summary>A fixed number of points per qualifying purchase, whatever its size.</summary>
    FlatPointsPerPurchase = 0,
    /// <summary>PointsPerRand points for every R1 spent (0.1 = 1 point per R10).</summary>
    PointsPerRandSpent = 1,
    /// <summary>CashBackPercentage % of the spend, converted to points at PointValueRands.</summary>
    CashBackPercentage = 2
}

/// <summary>Singleton row (Id always 1), same pattern as PricingSettings/SiteSettings. Off by default.</summary>
public class RewardsSettings
{
    [Key]
    public int Id { get; set; }

    public bool IsEnabled { get; set; } = false;
    public RewardEarnMode EarnMode { get; set; } = RewardEarnMode.PointsPerRandSpent;

    public int FlatPointsPerPurchase { get; set; } = 10;
    public decimal PointsPerRand { get; set; } = 0.1m;
    public decimal CashBackPercentage { get; set; } = 2m;

    /// <summary>What one point is worth in Rand when redeemed (also used to convert cash-back % into points).</summary>
    public decimal PointValueRands { get; set; } = 0.10m;

    /// <summary>Minimum spend (ex-VAT, after discount) for an order to earn anything.</summary>
    public decimal MinimumOrderAmount { get; set; } = 0m;

    public int MinimumPointsToRedeem { get; set; } = 100;

    /// <summary>Max share of an order's pre-VAT subtotal that points can cover.</summary>
    public decimal MaxRedeemPercentOfOrder { get; set; } = 50m;

    public DateTime DateUpdated { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}

public enum RewardsTransactionType
{
    Earned = 0,
    Redeemed = 1,
    /// <summary>Redeemed points handed back (payment failed, or the order was cancelled).</summary>
    RedemptionRestored = 2,
    /// <summary>Earned points taken back (return or cancellation).</summary>
    EarnReversed = 3
}

public class RewardsAccount
{
    [Key]
    public int RewardsAccountId { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    public int Balance { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public ICollection<RewardsTransaction> Transactions { get; set; } = new List<RewardsTransaction>();
}

/// <summary>Append-only ledger, same rule as WalletTransaction: corrections are new entries, never edits.
/// Points is always positive; direction comes from Type.</summary>
public class RewardsTransaction
{
    [Key]
    public int RewardsTransactionId { get; set; }

    public int RewardsAccountId { get; set; }
    public RewardsAccount? RewardsAccount { get; set; }

    public RewardsTransactionType Type { get; set; }
    public int Points { get; set; }
    public int BalanceAfter { get; set; }

    [MaxLength(60)]
    public string? Reference { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    [MaxLength(250)]
    public string? Note { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}