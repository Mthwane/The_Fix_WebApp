using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>
/// The trimmed "my workday" dashboard for floor staff (e.g. the Employee role). It holds ONLY what that person needs:
/// their own shift and till, their own sales, low stock, returns, and tickets that are theirs or waiting in the queue.
/// Every section is gated by a permission flag so a custom role still only sees what it holds.
/// </summary>
public class EmployeeDashboardViewModel
{
    // --- Time clocked in / POS money (PosUse) ---
    public bool ShowTill { get; set; }
    public ShiftSession? CurrentShift { get; set; }
    public decimal? ExpectedCash { get; set; }

    // --- Today's sales for THIS user's own till (PosUse) ---
    public decimal MySalesToday { get; set; }
    public int MyOrdersToday { get; set; }

    // --- Low stock + Restock Watchlist (LowStockView) ---
    public bool ShowLowStock { get; set; }
    public bool CanRaisePurchaseOrder { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public List<ProductVariant> LowStockVariants { get; set; } = new();

    // --- Returns (ReturnsProcess) ---
    public bool ShowReturns { get; set; }
    public int MyReturnsToday { get; set; }
    public int ReturnsInProgress { get; set; }

    // --- Tickets assigned to me or waiting in the queue (SupportTicketsManage) ---
    public bool ShowTickets { get; set; }
    public List<SupportTicket> MyTickets { get; set; } = new();
    public List<SupportTicket> QueueTickets { get; set; } = new();
    public int QueueTicketCount { get; set; }

    // --- Rolled-up "Needs Your Attention" ---
    public List<AttentionItem> AttentionItems { get; set; } = new();
}
