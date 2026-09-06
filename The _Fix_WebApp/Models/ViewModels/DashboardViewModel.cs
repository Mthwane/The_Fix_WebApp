using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>
/// Backs the staff dashboard (US-04, US-18, US-19). Every section corresponds to one
/// DashboardSections flag (see IDashboardService) - HomeController.Dashboard() only asks the
/// service to populate the sections the current user actually holds the permission for, so an
/// empty/default section here always means "not requested for this user", never "fetched but
/// genuinely empty" (that distinction lives on the specific list/count within a requested
/// section instead, e.g. RecentOrders.Count == 0 is a real, meaningful empty state).
/// </summary>
public class DashboardViewModel
{
    // --- Core KPIs (DashboardView - everyone who can reach this page) ---
    public decimal TodaysSales { get; set; }
    public int TodaysOrderCount { get; set; }
    public decimal MonthToDateRevenue { get; set; }
    public decimal AverageOrderValueToday { get; set; }
    public List<HourlyRevenuePoint> HourlyRevenueToday { get; set; } = new();
    public List<ChannelSplitItem> ChannelSplitToday { get; set; } = new();
    public List<Product> BestSellers { get; set; } = new();

    // --- Inventory & Catalogue (ProductsManage) ---
    public int TotalActiveProducts { get; set; }
    public int LowStockCount { get; set; }
    public List<ProductVariant> LowStockVariants { get; set; } = new();
    public int OutOfStockCount { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public List<Product> RecentlyAddedProducts { get; set; } = new();

    // --- Orders & Fulfillment (OrdersManage) ---
    public List<Order> RecentOrders { get; set; } = new();
    public Dictionary<OrderStatus, int> OrdersByStatus { get; set; } = new();
    public int OrdersNeedingAction { get; set; }
    public double? AvgFulfillmentHours { get; set; }
    public Dictionary<string, int> ShipmentsByStage { get; set; } = new();

    // --- Returns (ReturnsProcess) ---
    public int ReturnsToday { get; set; }
    public int ReturnsThisWeek { get; set; }
    public decimal RefundTotalThisWeek { get; set; }
    public double? ResalablePercentageThisWeek { get; set; }

    // --- Supply Chain (SuppliersManage / PurchaseOrdersManage) ---
    public Dictionary<PurchaseOrderStatus, int> PurchaseOrdersByStatus { get; set; } = new();
    public List<PurchaseOrder> UpcomingDeliveries { get; set; } = new();
    public int SuppliersWithIncompleteAddress { get; set; }
    public int ActiveRestockBundles { get; set; }

    // --- Approvals (PurchaseOrdersApprove specifically - narrower than SuppliersManage) ---
    public int PendingApprovalCount { get; set; }
    public decimal PendingApprovalValue { get; set; }

    // --- Reports (ReportsView) ---
    public Dictionary<PaymentMethod, decimal> SalesByPaymentMethod { get; set; } = new();
    public decimal DiscountsGivenToday { get; set; }
    public decimal DiscountsGivenMonthToDate { get; set; }
    public List<DepartmentRevenueItem> RevenueByDepartment { get; set; } = new();

    // --- Storefront / Merchandising (StorefrontManage) ---
    public int ReviewsThisWeek { get; set; }
    public List<ProductReview> RecentLowRatedReviews { get; set; } = new();
    public int WishlistAddsToday { get; set; }
    public bool TrendingIsCurated { get; set; }

    // --- Staff & Security (EmployeesManage / AuditLogsView) ---
    public int ActiveStaffCount { get; set; }
    public int StaffWithout2FA { get; set; }
    public List<AuditLog> RecentActivity { get; set; } = new();

    // --- Till / Shift (PosUse to see your own; ReportsView for the full history) ---
    public ShiftSession? CurrentShift { get; set; }
    public decimal? CurrentShiftExpectedCash { get; set; }
    public List<ShiftSession> RecentShifts { get; set; } = new();

    // --- Rolled-up cross-domain callouts, built last from whatever sections were populated ---
    public List<AttentionItem> AttentionItems { get; set; } = new();
}

public class HourlyRevenuePoint
{
    public int Hour { get; set; }
    public decimal Revenue { get; set; }
}

public class ChannelSplitItem
{
    public string Channel { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
}

public class DepartmentRevenueItem
{
    public string DepartmentName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal Percentage { get; set; }
}

/// <summary>A rolled-up, cross-domain "needs your attention" callout - each one links straight
/// to the screen that resolves it. Severity is a display hint only (info/warning/danger).</summary>
public class AttentionItem
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
}
