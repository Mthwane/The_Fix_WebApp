using FashionFix.Web.Data;
using FashionFix.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: / - the branded login page. Signed-in users get bounced to the dashboard.
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction(nameof(Dashboard));

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    // GET: /Home/Dashboard - dashboard of business statistics (US-04, US-05).
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Dashboard()
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var model = new DashboardViewModel
        {
            TodaysSales = await _context.Orders
                .Where(o => o.DateCreated >= today)
                .SumAsync(o => (decimal?)o.GrandTotal) ?? 0,

            TodaysOrderCount = await _context.Orders
                .CountAsync(o => o.DateCreated >= today),

            MonthToDateRevenue = await _context.Orders
                .Where(o => o.DateCreated >= monthStart)
                .SumAsync(o => (decimal?)o.GrandTotal) ?? 0,

            TotalActiveProducts = await _context.Products.CountAsync(p => p.IsActive),

            LowStockProducts = await _context.Products
                .Where(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold)
                .OrderBy(p => p.StockQuantity)
                .Take(10)
                .ToListAsync()
        };

        model.LowStockCount = model.LowStockProducts.Count;

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Error() => View();
}
