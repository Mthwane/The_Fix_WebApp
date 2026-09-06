using FashionFix.Web.Data;
using FashionFix.Web.Models;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

/// <summary>
/// Computes every dashboard section as a set of direct, live queries - no caching, no
/// snapshot table. Deliberate: this is a single-store app, so the query volume here is cheap
/// enough that a cache would be solving a problem that doesn't exist yet. If this ever becomes
/// multi-store or high-volume, that's the signal to revisit (see query cost in logs), not a
/// reason to add caching pre-emptively now.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _context;

    public DashboardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardViewModel> BuildAsync(DashboardSections sections, string? currentUserId = null)
    {
        var model = new DashboardViewModel();
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var weekStart = today.AddDays(-7);

        if (sections.HasFlag(DashboardSections.CoreKpis))
            await PopulateCoreKpisAsync(model, today, monthStart);

        if (sections.HasFlag(DashboardSections.Inventory))
            await PopulateInventoryAsync(model);

        if (sections.HasFlag(DashboardSections.Orders))
            await PopulateOrdersAsync(model, weekStart);

        if (sections.HasFlag(DashboardSections.Returns))
            await PopulateReturnsAsync(model, today, weekStart);

        if (sections.HasFlag(DashboardSections.SupplyChain))
            await PopulateSupplyChainAsync(model, today);

        if (sections.HasFlag(DashboardSections.Approvals))
            await PopulateApprovalsAsync(model);

        if (sections.HasFlag(DashboardSections.Reports))
            await PopulateReportsAsync(model, today, monthStart);

        if (sections.HasFlag(DashboardSections.Storefront))
            await PopulateStorefrontAsync(model, weekStart, today);

        if (sections.HasFlag(DashboardSections.Staff))
            await PopulateStaffAsync(model);

        if (sections.HasFlag(DashboardSections.AuditActivity))
            model.RecentActivity = await _context.AuditLogs.AsNoTracking()
                .Include(a => a.User)
                .OrderByDescending(a => a.Timestamp)
                .Take(15)
                .ToListAsync();

        if (sections.HasFlag(DashboardSections.Shift))
            await PopulateShiftAsync(model, currentUserId, sections);

        if (sections.HasFlag(DashboardSections.AccessControl))
            await PopulateAccessControlAsync(model);

        model.AttentionItems = BuildAttentionItems(model, sections);

        return model;
    }

    private async Task PopulateCoreKpisAsync(DashboardViewModel model, DateTime today, DateTime monthStart)
    {
        var todaysOrders = await _context.Orders.AsNoTracking()
            .Where(o => o.DateCreated >= today)
            .Select(o => new { o.GrandTotal, o.DateCreated, o.OrderType })
            .ToListAsync();

        model.TodaysSales = todaysOrders.Sum(o => o.GrandTotal);
        model.TodaysOrderCount = todaysOrders.Count;
        model.AverageOrderValueToday = todaysOrders.Count > 0 ? todaysOrders.Average(o => o.GrandTotal) : 0;

        model.MonthToDateRevenue = await _context.Orders.AsNoTracking()
            .Where(o => o.DateCreated >= monthStart)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;

        model.HourlyRevenueToday = todaysOrders
            .GroupBy(o => o.DateCreated.ToLocalTime().Hour)
            .Select(g => new HourlyRevenuePoint { Hour = g.Key, Revenue = g.Sum(o => o.GrandTotal) })
            .OrderBy(h => h.Hour)
            .ToList();

        model.ChannelSplitToday = todaysOrders
            .GroupBy(o => o.OrderType)
            .Select(g => new ChannelSplitItem { Channel = g.Key.ToString(), OrderCount = g.Count(), Revenue = g.Sum(o => o.GrandTotal) })
            .ToList();

        model.BestSellers = await _context.OrderItems.AsNoTracking()
            .Where(oi => oi.Order.DateCreated >= monthStart)
            .GroupBy(oi => oi.ProductId)
            .OrderByDescending(g => g.Sum(oi => oi.Quantity))
            .Take(5)
            .Select(g => g.First().Product)
            .ToListAsync();
    }

    private async Task PopulateInventoryAsync(DashboardViewModel model)
    {
        model.TotalActiveProducts = await _context.Products.CountAsync(p => p.IsActive);

        model.LowStockVariants = await _context.ProductVariants.AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product.IsActive && v.StockQuantity <= v.Product.LowStockThreshold)
            .OrderBy(v => v.StockQuantity)
            .Take(10)
            .ToListAsync();
        model.LowStockCount = await _context.ProductVariants.AsNoTracking()
            .CountAsync(v => v.IsActive && v.Product.IsActive && v.StockQuantity <= v.Product.LowStockThreshold);

        model.OutOfStockCount = await _context.ProductVariants.AsNoTracking()
            .CountAsync(v => v.IsActive && v.Product.IsActive && v.StockQuantity == 0);

        model.TotalInventoryValue = await _context.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive)
            .SumAsync(v => (decimal?)(v.StockQuantity * v.Product.CostPrice)) ?? 0;

        model.RecentlyAddedProducts = await _context.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.DateAdded)
            .Take(5)
            .ToListAsync();
    }

    private async Task PopulateOrdersAsync(DashboardViewModel model, DateTime weekStart)
    {
        model.RecentOrders = await _context.Orders.AsNoTracking()
            .OrderByDescending(o => o.DateCreated)
            .Take(5)
            .ToListAsync();

        model.OrdersByStatus = (await _context.Orders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.Status, x => x.Count);

        model.OrdersNeedingAction = model.OrdersByStatus.GetValueOrDefault(OrderStatus.Pending)
            + model.OrdersByStatus.GetValueOrDefault(OrderStatus.Processing);

        var fulfilledRecently = await _context.Orders.AsNoTracking()
            .Where(o => o.DateFulfilled != null && o.DateCreated >= weekStart)
            .Select(o => new { o.DateCreated, DateFulfilled = o.DateFulfilled!.Value })
            .ToListAsync();
        model.AvgFulfillmentHours = fulfilledRecently.Count > 0
            ? fulfilledRecently.Average(o => (o.DateFulfilled - o.DateCreated).TotalHours)
            : null;

        model.ShipmentsByStage = (await _context.CourierShipments.AsNoTracking()
            .Where(s => s.OrderId != null)
            .ToListAsync()) // Stage is a computed property, not a column - group in memory
            .GroupBy(s => s.Stage)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task PopulateReturnsAsync(DashboardViewModel model, DateTime today, DateTime weekStart)
    {
        model.ReturnsToday = await _context.ReturnTransactions.AsNoTracking().CountAsync(r => r.DateProcessed >= today);

        var thisWeek = await _context.ReturnTransactions.AsNoTracking()
            .Where(r => r.DateProcessed >= weekStart)
            .Select(r => new { r.RefundAmount, r.IsResalable })
            .ToListAsync();

        model.ReturnsThisWeek = thisWeek.Count;
        model.RefundTotalThisWeek = thisWeek.Sum(r => r.RefundAmount);
        model.ResalablePercentageThisWeek = thisWeek.Count > 0
            ? 100.0 * thisWeek.Count(r => r.IsResalable) / thisWeek.Count
            : null;
    }

    private async Task PopulateSupplyChainAsync(DashboardViewModel model, DateTime today)
    {
        model.PurchaseOrdersByStatus = (await _context.PurchaseOrders.AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.Status, x => x.Count);

        model.UpcomingDeliveries = await _context.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.DateExpected != null && p.DateExpected >= today && p.DateExpected <= today.AddDays(7)
                && p.Status != PurchaseOrderStatus.Cancelled && p.Status != PurchaseOrderStatus.Received)
            .OrderBy(p => p.DateExpected)
            .Take(10)
            .ToListAsync();

        model.SuppliersWithIncompleteAddress = await _context.Suppliers.AsNoTracking()
            .CountAsync(s => string.IsNullOrEmpty(s.StreetAddress) || string.IsNullOrEmpty(s.City));

        model.ActiveRestockBundles = await _context.RestockBundles.AsNoTracking().CountAsync(b => b.IsActive);
    }

    private async Task PopulateApprovalsAsync(DashboardViewModel model)
    {
        var pending = await _context.PurchaseOrders.AsNoTracking()
            .Where(p => p.Status == PurchaseOrderStatus.AwaitingApproval)
            .ToListAsync();

        model.PendingApprovalCount = pending.Count;
        model.PendingApprovalValue = pending.Sum(p => p.TotalCost);
    }

    private async Task PopulateReportsAsync(DashboardViewModel model, DateTime today, DateTime monthStart)
    {
        model.SalesByPaymentMethod = (await _context.Orders.AsNoTracking()
            .Where(o => o.DateCreated >= monthStart)
            .GroupBy(o => o.PaymentMethod)
            .Select(g => new { Method = g.Key, Total = g.Sum(o => o.GrandTotal) })
            .ToListAsync())
            .ToDictionary(x => x.Method, x => x.Total);

        model.DiscountsGivenToday = await _context.Orders.AsNoTracking()
            .Where(o => o.DateCreated >= today)
            .SumAsync(o => (decimal?)o.DiscountTotal) ?? 0;
        model.DiscountsGivenMonthToDate = await _context.Orders.AsNoTracking()
            .Where(o => o.DateCreated >= monthStart)
            .SumAsync(o => (decimal?)o.DiscountTotal) ?? 0;

        var deptRevenue = await _context.OrderItems.AsNoTracking()
            .Where(oi => oi.Order.DateCreated >= monthStart && oi.Product.DepartmentId != null)
            .GroupBy(oi => oi.Product.Department!.Name)
            .Select(g => new { Department = g.Key, Revenue = g.Sum(oi => oi.LineTotal) })
            .ToListAsync();
        var totalDeptRevenue = deptRevenue.Sum(d => d.Revenue);
        model.RevenueByDepartment = deptRevenue
            .Select(d => new DepartmentRevenueItem
            {
                DepartmentName = d.Department,
                Revenue = d.Revenue,
                Percentage = totalDeptRevenue > 0 ? Math.Round(100 * d.Revenue / totalDeptRevenue, 1) : 0
            })
            .OrderByDescending(d => d.Revenue)
            .ToList();
    }

    private async Task PopulateStorefrontAsync(DashboardViewModel model, DateTime weekStart, DateTime today)
    {
        model.ReviewsThisWeek = await _context.ProductReviews.AsNoTracking().CountAsync(r => r.DateCreated >= weekStart);

        model.RecentLowRatedReviews = await _context.ProductReviews.AsNoTracking()
            .Include(r => r.Product)
            .Include(r => r.Customer)
            .Where(r => r.DateCreated >= weekStart && r.Rating <= 2)
            .OrderByDescending(r => r.DateCreated)
            .Take(5)
            .ToListAsync();

        model.WishlistAddsToday = await _context.WishlistItems.AsNoTracking().CountAsync(w => w.DateAdded >= today);

        model.TrendingIsCurated = await _context.FeaturedProducts.AsNoTracking().AnyAsync(f => f.IsActive);
    }

    private async Task PopulateStaffAsync(DashboardViewModel model)
    {
        var staffUserIds = await _context.UserRoles
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => AppRoles.StaffRoles.Contains(x.Name))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync();

        model.ActiveStaffCount = await _context.Users.AsNoTracking()
            .CountAsync(u => staffUserIds.Contains(u.Id) && u.IsActive);

        model.StaffWithout2FA = await _context.Users.AsNoTracking()
            .CountAsync(u => staffUserIds.Contains(u.Id) && u.IsActive && !u.TwoFactorEnabled);
    }

    private async Task PopulateShiftAsync(DashboardViewModel model, string? currentUserId, DashboardSections sections)
    {
        if (!string.IsNullOrEmpty(currentUserId))
        {
            model.CurrentShift = await _context.ShiftSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == currentUserId && s.Status == ShiftStatus.Open);

            if (model.CurrentShift is not null)
            {
                var cashSalesSinceOpen = await _context.Orders.AsNoTracking()
                    .Where(o => o.ProcessedByUserId == currentUserId
                        && o.PaymentMethod == PaymentMethod.Cash
                        && o.DateCreated >= model.CurrentShift.DateOpened)
                    .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;

                model.CurrentShiftExpectedCash = model.CurrentShift.OpeningFloat + cashSalesSinceOpen;
            }
        }

        // Full shift history, and who's currently on the floor, are manager/reports-level -
        // not something every till operator needs cluttering their own dashboard.
        if (sections.HasFlag(DashboardSections.Reports))
        {
            model.RecentShifts = await _context.ShiftSessions.AsNoTracking()
                .Include(s => s.User)
                .OrderByDescending(s => s.DateOpened)
                .Take(10)
                .ToListAsync();

            model.OpenShifts = await _context.ShiftSessions.AsNoTracking()
                .Include(s => s.User)
                .Where(s => s.Status == ShiftStatus.Open)
                .OrderBy(s => s.DateOpened)
                .ToListAsync();
        }
    }

    // Read-only preview of the exact same role/claim data the Roles & Permissions screen
    // edits (RolesController) - queried directly here rather than via RoleManager, since this
    // never writes anything and a plain query is cheaper than spinning up the full manager.
    private async Task PopulateAccessControlAsync(DashboardViewModel model)
    {
        var roles = await _context.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync();
        model.RbacRoleNames = roles.Select(r => r.Name!).ToList();

        var claimsByRole = await _context.RoleClaims.AsNoTracking()
            .Where(c => c.ClaimType == Permissions.ClaimType)
            .ToListAsync();

        model.RbacMatrix = Permissions.All.Select(kv => new RbacMatrixRow
        {
            PermissionLabel = kv.Value,
            GrantedByRole = roles.ToDictionary(
                r => r.Name!,
                r => claimsByRole.Any(c => c.RoleId == r.Id && c.ClaimValue == kv.Key))
        }).ToList();
    }

    private static List<AttentionItem> BuildAttentionItems(DashboardViewModel model, DashboardSections sections)
    {
        var items = new List<AttentionItem>();

        if (sections.HasFlag(DashboardSections.Approvals) && model.PendingApprovalCount > 0)
            items.Add(new AttentionItem { Label = "Purchase orders awaiting your approval", Count = model.PendingApprovalCount, Url = "/PurchaseOrders?status=AwaitingApproval", Severity = "warning" });

        if (sections.HasFlag(DashboardSections.Inventory) && model.LowStockCount > 0)
            items.Add(new AttentionItem { Label = "Items low or out of stock", Count = model.LowStockCount, Url = "/Products/LowStock", Severity = model.OutOfStockCount > 0 ? "danger" : "warning" });

        if (sections.HasFlag(DashboardSections.SupplyChain) && model.SuppliersWithIncompleteAddress > 0)
            items.Add(new AttentionItem { Label = "Suppliers with an incomplete collection address", Count = model.SuppliersWithIncompleteAddress, Url = "/Suppliers", Severity = "warning" });

        if (sections.HasFlag(DashboardSections.Orders) && model.OrdersNeedingAction > 0)
            items.Add(new AttentionItem { Label = "Orders needing action", Count = model.OrdersNeedingAction, Url = "/Orders", Severity = "info" });

        if (sections.HasFlag(DashboardSections.Storefront) && model.RecentLowRatedReviews.Count > 0)
            items.Add(new AttentionItem { Label = "Low-rated reviews this week", Count = model.RecentLowRatedReviews.Count, Url = "/Storefront/Reviews", Severity = "warning" });

        if (sections.HasFlag(DashboardSections.Staff) && model.StaffWithout2FA > 0)
            items.Add(new AttentionItem { Label = "Staff accounts without 2FA enabled", Count = model.StaffWithout2FA, Url = "/Employees", Severity = "warning" });

        return items;
    }
}
