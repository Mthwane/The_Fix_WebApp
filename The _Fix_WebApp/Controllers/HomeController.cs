using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: / - the branded customer login page. Signed-in users get bounced to the
    // page that matches their role (dashboard for staff, order history for customers).
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return await RedirectToRoleHome();

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    // GET: /Home/Dashboard - dashboard of business statistics (US-04, US-05).
    // Staff-only: customers get their own account area instead (see CustomerController).
    [HttpGet]
    [Authorize(Policy = Permissions.DashboardView)]
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
                .ToListAsync(),

            RecentOrders = await _context.Orders
                .OrderByDescending(o => o.DateCreated)
                .Take(5)
                .ToListAsync()
        };

        model.LowStockCount = model.LowStockProducts.Count;

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Error() => View();

    private async Task<IActionResult> RedirectToRoleHome()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is not null)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Customer") &&
                !roles.Any(r => r is "Administrator" or "Manager" or "Employee" or "Owner"))
            {
                return RedirectToAction("Orders", "Customer");
            }
        }

        return RedirectToAction(nameof(Dashboard));
    }
}
