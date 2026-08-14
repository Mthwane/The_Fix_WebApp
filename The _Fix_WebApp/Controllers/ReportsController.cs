using FashionFix.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

[Authorize(Roles = "Administrator,Manager,Owner")]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ReportsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Reports?from=&to= - sales, income and expense overview (US-18).
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;

        var orders = await _context.Orders
            .Where(o => o.DateCreated >= start && o.DateCreated <= end)
            .ToListAsync();

        ViewBag.TotalRevenue = orders.Sum(o => o.GrandTotal);
        ViewBag.OrderCount = orders.Count;
        ViewBag.From = start;
        ViewBag.To = end;

        // TODO: extend with best-selling items, stock turnover, and revenue-by-category
        // aggregate queries once seed data exists, then wire up PDF/CSV export.
        return View(orders);
    }

    // GET: /Reports/Inventory - stock turnover / low-stock overview.
    [HttpGet]
    public IActionResult Inventory() => View();

    // GET: /Reports/Employees - employee performance overview.
    [HttpGet]
    public IActionResult Employees() => View();

    // GET: /Reports/Export?format=pdf|csv
    [HttpGet]
    public IActionResult Export(string format)
    {
        // TODO: implement PDF/CSV export of the current report view.
        return NotFound();
    }
}
