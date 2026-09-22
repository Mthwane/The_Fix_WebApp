using System.Text;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

[Authorize(Policy = Permissions.ReportsView)]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ReportsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Reports?from=&to=&channel= - sales, margin and channel overview (US-18, US-19).
    // Every figure here is computed live from Orders/OrderItems - nothing is a "batch" or
    // pre-aggregated rollup, so it's always consistent with what Orders/POS actually show.
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, OrderType? channel)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = (to ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

        var ordersQuery = _context.Orders.AsNoTracking()
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product).ThenInclude(p => p!.Department)
            .Where(o => o.DateCreated >= start && o.DateCreated <= end);

        if (channel.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.OrderType == channel.Value);
        }

        var orders = await ordersQuery.ToListAsync();
        var allItems = orders.SelectMany(o => o.OrderItems).ToList();

        decimal Cogs(IEnumerable<OrderItem> items) => items.Sum(oi => oi.Quantity * (oi.Product?.CostPrice ?? 0));

        var totalRevenue = orders.Sum(o => o.GrandTotal);
        var costOfGoodsSold = Cogs(allItems);

        ViewBag.TotalRevenue = totalRevenue;
        ViewBag.EstimatedExpenses = costOfGoodsSold;
        ViewBag.EstimatedProfit = totalRevenue - costOfGoodsSold;
        ViewBag.OrderCount = orders.Count;
        ViewBag.UnitsDispatched = allItems.Sum(oi => oi.Quantity);
        ViewBag.VatCollected = orders.Sum(o => o.TaxTotal);
        ViewBag.DiscountsGiven = orders.Sum(o => o.DiscountTotal);
        ViewBag.From = start;
        ViewBag.To = to ?? DateTime.UtcNow;
        ViewBag.SelectedChannel = channel;

        ViewBag.BestSellers = allItems
            .GroupBy(oi => oi.Product?.Name ?? "Unknown")
            .Select(g => new { Name = g.Key, UnitsSold = g.Sum(oi => oi.Quantity), Revenue = g.Sum(oi => oi.LineTotal) })
            .OrderByDescending(g => g.UnitsSold)
            .Take(5)
            .ToList();

        // Revenue by category, split by channel (POS vs Online) - backs both the category
        // breakdown bars and the channel-comparison grouped bars in the view.
        ViewBag.RevenueByCategory = allItems
            .GroupBy(oi => oi.Product?.Category ?? "Uncategorized")
            .Select(g => new
            {
                Category = g.Key,
                Revenue = g.Sum(oi => oi.LineTotal),
                PosRevenue = g.Where(oi => oi.Order.OrderType == OrderType.POS).Sum(oi => oi.LineTotal),
                OnlineRevenue = g.Where(oi => oi.Order.OrderType == OrderType.Online).Sum(oi => oi.LineTotal)
            })
            .OrderByDescending(g => g.Revenue)
            .ToList();

        ViewBag.ChannelSplit = orders
            .GroupBy(o => o.OrderType)
            .Select(g => new { Channel = g.Key.ToString(), Revenue = g.Sum(o => o.GrandTotal), Count = g.Count() })
            .ToList();

        // Daily revenue trend for the selected range - capped to the last 60 days of buckets
        // even if a longer range is requested, so the chart stays legible.
        var trendStart = start > end.AddDays(-60) ? start.Date : end.Date.AddDays(-60);
        ViewBag.DailyTrend = orders
            .Where(o => o.DateCreated.Date >= trendStart)
            .GroupBy(o => o.DateCreated.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.GrandTotal) })
            .OrderBy(g => g.Date)
            .ToList();

        var deptRevenue = allItems
            .Where(oi => oi.Product?.DepartmentId != null)
            .GroupBy(oi => oi.Product!.Department!.Name)
            .Select(g => new { Department = g.Key, Revenue = g.Sum(oi => oi.LineTotal) })
            .OrderByDescending(g => g.Revenue)
            .ToList();
        ViewBag.RevenueByDepartment = deptRevenue;

        // Honest, computable stand-ins for "inventory velocity" - not day-by-day aged stock
        // (nothing tracks a per-unit received date), but two real numbers that mean something:
        // how fast suppliers actually replenish, and how much of on-hand stock sold in-range.
        var activeSuppliers = await _context.Suppliers.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        ViewBag.AvgSupplierLeadTimeDays = activeSuppliers.Count > 0 ? activeSuppliers.Average(s => s.LeadTimeDays) : (double?)null;

        var unitsSoldInRange = allItems.Sum(oi => oi.Quantity);
        var currentTotalStock = await _context.ProductVariants.AsNoTracking().Where(v => v.IsActive).SumAsync(v => v.StockQuantity);
        ViewBag.SellThroughRate = (unitsSoldInRange + currentTotalStock) > 0
            ? (double)unitsSoldInRange / (unitsSoldInRange + currentTotalStock) * 100
            : (double?)null;

        // Per-order margin, for the "recent transactions" table - real per-order figures,
        // never a fabricated "batch".
        ViewBag.RecentTransactions = orders
            .OrderByDescending(o => o.DateCreated)
            .Take(10)
            .Select(o => new
            {
                o.OrderNumber,
                o.DateCreated,
                o.OrderType,
                Gmv = o.GrandTotal,
                Vat = o.TaxTotal,
                Cogs = Cogs(o.OrderItems),
                Margin = o.GrandTotal - o.TaxTotal - Cogs(o.OrderItems)
            })
            .ToList();

        return View(orders);
    }

    // GET: /Reports/Export?format=csv - export the sales report for the given date range.
    [HttpGet]
    public async Task<IActionResult> Export(string format, DateTime? from, DateTime? to, OrderType? channel)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = (to ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

        var query = _context.Orders.AsNoTracking()
            .Where(o => o.DateCreated >= start && o.DateCreated <= end);
        if (channel.HasValue) query = query.Where(o => o.OrderType == channel.Value);

        var orders = await query.OrderBy(o => o.DateCreated).ToListAsync();

        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only CSV export is currently supported.");

        var sb = new StringBuilder();
        sb.AppendLine("OrderNumber,Date,Type,Status,PaymentMethod,SubTotal,Discount,Tax,GrandTotal");
        foreach (var o in orders)
        {
            sb.AppendLine($"{o.OrderNumber},{o.DateCreated:yyyy-MM-dd HH:mm},{o.OrderType},{o.Status},{o.PaymentMethod},{o.SubTotal},{o.DiscountTotal},{o.TaxTotal},{o.GrandTotal}");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"sales-report-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
    }
}
