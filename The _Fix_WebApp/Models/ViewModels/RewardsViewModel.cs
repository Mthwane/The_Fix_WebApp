using System.ComponentModel.DataAnnotations;
using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

public class RedemptionQuote
{
    public int Balance { get; set; }
    public bool CanRedeem { get; set; }
    public int MaxPoints { get; set; }
    public decimal MaxDiscount { get; set; }
    public string? Reason { get; set; }
}

public class RewardsOverview
{
    public int MembersWithPoints { get; set; }
    public long OutstandingPoints { get; set; }
    public decimal OutstandingLiability { get; set; }
    public long EarnedLast30Days { get; set; }
    public long RedeemedLast30Days { get; set; }
}

public class EarnPreviewRow
{
    public decimal PurchaseAmount { get; set; }
    public int Points { get; set; }
    public decimal Value { get; set; }
}

public class RewardsSettingsViewModel
{
    [Display(Name = "Rewards programme active")]
    public bool IsEnabled { get; set; }

    [Display(Name = "Earn mode")]
    public RewardEarnMode EarnMode { get; set; }

    [Range(0, 100000, ErrorMessage = "Points per purchase must be between 0 and 100,000.")]
    public int FlatPointsPerPurchase { get; set; }

    [Range(0, 1000, ErrorMessage = "Points per R1 must be between 0 and 1,000.")]
    public decimal PointsPerRand { get; set; }

    [Range(0, 100, ErrorMessage = "Cash-back must be between 0% and 100%.")]
    public decimal CashBackPercentage { get; set; }

    [Range(0.01, 1000, ErrorMessage = "Point value must be between R0.01 and R1,000.")]
    public decimal PointValueRands { get; set; }

    [Range(0, 1000000, ErrorMessage = "Minimum order amount must be between R0 and R1,000,000.")]
    public decimal MinimumOrderAmount { get; set; }

    [Range(0, 1000000, ErrorMessage = "Minimum points to redeem must be between 0 and 1,000,000.")]
    public int MinimumPointsToRedeem { get; set; }

    [Range(0, 90, ErrorMessage = "Max redemption must be between 0% and 90% (an order can't be fully covered by points).")]
    public decimal MaxRedeemPercentOfOrder { get; set; }

    public RewardsOverview Overview { get; set; } = new();
    public List<EarnPreviewRow> Preview { get; set; } = new();
}

public class RewardsCustomerViewModel
{
    public int Balance { get; set; }
    public decimal BalanceValue { get; set; }
    public RewardsSettings Settings { get; set; } = new();
    public string EarnRule { get; set; } = string.Empty;
    public List<RewardsTransaction> History { get; set; } = new();
}