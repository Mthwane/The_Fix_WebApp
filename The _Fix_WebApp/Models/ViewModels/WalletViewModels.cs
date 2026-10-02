using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>Customer-facing FixCash page: balance plus one page of history.</summary>
public class WalletIndexViewModel
{
    public decimal Balance { get; set; }
    public List<WalletTransaction> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}

/// <summary>One row in a staff wallet list.</summary>
public class WalletAdminListItem
{
    public string CustomerId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public decimal Balance { get; set; }
    public DateTime? LastActivity { get; set; }
}

public class WalletAdminIndexViewModel
{
    public List<WalletAdminListItem> Items { get; set; } = new();
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    /// <summary>Sum of balances across every wallet matching the current search (not just this page).</summary>
    public decimal TotalBalance { get; set; }
}

public class WalletAdminDetailsViewModel
{
    public ApplicationUser Customer { get; set; } = null!;
    public decimal Balance { get; set; }
    public List<WalletTransaction> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public bool CanAdjust { get; set; }
}

public class WalletMovementRow
{
    public string Label { get; set; } = string.Empty;
    public bool IsCredit { get; set; }
    public int Count { get; set; }
    public decimal Total { get; set; }
}

/// <summary>What the business owes customers in FixCash, plus an integrity check on the ledger.</summary>
public class WalletLiabilityViewModel
{
    /// <summary>Report period, in South African dates (inclusive).</summary>
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public decimal TotalLiability { get; set; }
    public int TotalWallets { get; set; }
    public int WalletsWithBalance { get; set; }

    /// <summary>Net of every ledger entry ever written (credits minus debits). Should equal TotalLiability.</summary>
    public decimal LedgerNet { get; set; }
    public decimal Discrepancy => TotalLiability - LedgerNet;

    public List<WalletMovementRow> Movements { get; set; } = new();

    public int DormantDays { get; set; }
    public int DormantWallets { get; set; }
    public decimal DormantAmount { get; set; }

    public List<WalletAdminListItem> TopBalances { get; set; } = new();
}
