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

    // GET: /Reports?from=&to= - sales, income and expense overview (US-18, US-19).
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = (to ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

        var orders = await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .Where(o => o.DateCreated >= start && o.DateCreated <= end)
            .ToListAsync();

        var costOfGoodsSold = orders
            .SelectMany(o => o.OrderItems)
            .Sum(oi => oi.Quantity * (oi.Product?.CostPrice ?? 0));

        var bestSellers = orders
            .SelectMany(o => o.OrderItems)
            .GroupBy(oi => oi.Product?.Name ?? "Unknown")
            .Select(g => new { Name = g.Key, UnitsSold = g.Sum(oi => oi.Quantity), Revenue = g.Sum(oi => oi.LineTotal) })
            .OrderByDescending(g => g.UnitsSold)
            .Take(5)
            .ToList();

        var revenueByCategory = orders
            .SelectMany(o => o.OrderItems)
            .GroupBy(oi => oi.Product?.Category ?? "Uncategorized")
            .Select(g => new { Category = g.Key, Revenue = g.Sum(oi => oi.LineTotal) })
            .OrderByDescending(g => g.Revenue)
            .ToList();

        ViewBag.TotalRevenue = orders.Sum(o => o.GrandTotal);
        ViewBag.EstimatedExpenses = costOfGoodsSold;
        ViewBag.EstimatedProfit = orders.Sum(o => o.GrandTotal) - costOfGoodsSold;
        ViewBag.OrderCount = orders.Count;
        ViewBag.From = start;
        ViewBag.To = to ?? DateTime.UtcNow;
        ViewBag.BestSellers = bestSellers;
        ViewBag.RevenueByCategory = revenueByCategory;

        return View(orders);
    }

    // GET: /Reports/Inventory - stock turnover / low-stock overview.
    [HttpGet]
    public async Task<IActionResult> Inventory()
    {
        var products = await _context.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.StockQuantity)
            .ToListAsync();

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var unitsSoldLast30Days = await _context.OrderItems
            .Where(oi => oi.Order.DateCreated >= thirtyDaysAgo)
            .GroupBy(oi => oi.ProductId)
            .Select(g => new { ProductId = g.Key, Units = g.Sum(oi => oi.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Units);

        ViewBag.UnitsSoldLast30Days = unitsSoldLast30Days;
        return View(products);
    }

    // GET: /Reports/Employees - employee performance overview (sales processed per staff member).
    [HttpGet]
    public async Task<IActionResult> Employees()
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var performance = await _context.Orders
            .Where(o => o.DateCreated >= thirtyDaysAgo && o.ProcessedByUserId != null)
            .Include(o => o.ProcessedByUser)
            .GroupBy(o => new { o.ProcessedByUserId, o.ProcessedByUser!.FullName })
            .Select(g => new
            {
                g.Key.FullName,
                SalesCount = g.Count(),
                Revenue = g.Sum(o => o.GrandTotal)
            })
            .OrderByDescending(g => g.Revenue)
            .ToListAsync();

        return View(performance);
    }

    // GET: /Reports/Export?format=csv - export the sales report for the given date range.
    [HttpGet]
    public async Task<IActionResult> Export(string format, DateTime? from, DateTime? to)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = (to ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

        var orders = await _context.Orders
            .Where(o => o.DateCreated >= start && o.DateCreated <= end)
            .OrderBy(o => o.DateCreated)
            .ToListAsync();

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
