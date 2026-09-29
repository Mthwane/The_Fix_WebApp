using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum WalletTransactionType
{
    /// <summary>Money added via Paystack.</summary>
    Deposit,
    /// <summary>Spent on an order at checkout.</summary>
    Spend,
    /// <summary>Credited back from a return processed with RefundMethod.StoreCredit.</summary>
    RefundCredit,
    /// <summary>A manual correction by staff (e.g. goodwill credit, error fix) - not yet
    /// exposed in any UI, but the ledger is ready for it.</summary>
    AdminAdjustment
}

/// <summary>One customer's FixCash balance. Deliberately thin - Balance is a running total kept
/// in lockstep with WalletTransaction (see WalletService), not derived by summing the ledger on
/// every read, so checking a balance stays a single indexed lookup.</summary>
public class WalletAccount
{
    [Key]
    public int WalletAccountId { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Balance { get; set; }

    /// <summary>EF Core concurrency token - two simultaneous debits/credits against the same
    /// wallet (e.g. a refund landing at the same moment as a checkout spend) must never silently
    /// overwrite one another. See WalletService.RunWithRetryAsync for how this gets used.</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
}

/// <summary>
/// One entry in a wallet's append-only ledger (US-fixcash-1). Never updated or deleted once
/// written - a correction is a new offsetting entry, never an edit to history. This is what
/// makes the wallet auditable: WalletAccount.Balance should always equal the sum of its
/// transactions' signed amounts, and if it ever doesn't, this table is how you'd find out why.
/// </summary>
public class WalletTransaction
{
    [Key]
    public int WalletTransactionId { get; set; }

    public int WalletAccountId { get; set; }
    public WalletAccount? WalletAccount { get; set; }

    public WalletTransactionType Type { get; set; }

    /// <summary>Always positive - direction comes from Type, not the sign of this field.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>The account balance immediately after this entry, for audit/reconciliation -
    /// not used to derive the current balance (WalletAccount.Balance is authoritative for that).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }

    /// <summary>What this ties back to: the Paystack reference for a Deposit, the order number
    /// for a Spend/RefundCredit. For a Deposit specifically, this is also the idempotency key -
    /// see WalletService.CreditFromDepositAsync.</summary>
    [MaxLength(60)]
    public string? Reference { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    [MaxLength(250)]
    public string? Note { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}
