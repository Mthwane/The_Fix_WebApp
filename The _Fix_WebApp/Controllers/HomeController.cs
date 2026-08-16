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
    private readonly ILogger<HomeController> _logger;

    public HomeController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ILogger<HomeController> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
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
    // Always queries live from the database - no caching - so it reflects every sale,
    // return, and stock change the moment it happens.
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

        // Low-stock notification (US-03): surfaces as a toast every time a staff member
        // lands on the dashboard while items are below threshold, on top of the table below.
        if (model.LowStockCount > 0 && Can(Permissions.ProductsManage))
        {
            var names = string.Join(", ", model.LowStockProducts.Take(3).Select(p => p.Name));
            var suffix = model.LowStockCount > 3 ? $" and {model.LowStockCount - 3} more" : "";
            this.ToastWarning($"Low stock: {names}{suffix}.");
        }

        return View(model);
    }

    // POST: /Home/LogClientError - best-effort sink for uncaught JS errors, so a failure
    // in the browser (not just the server) still ends up in the logs somewhere.
    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult LogClientError([FromBody] ClientErrorReport report)
    {
        _logger.LogWarning(
            "Client-side JS error: {Message} at {Source}:{Line} (url: {Url}, user: {User})",
            report.Message, report.Source, report.Line, report.Url, User.Identity?.Name ?? "anonymous");

        return Ok();
    }

    // GET: /Home/Error - fallback screen for unhandled server exceptions (see Program.cs
    // app.UseExceptionHandler). The exception itself is already logged by the framework's
    // exception handler middleware before it ever reaches this action.
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Error()
    {
        ViewBag.RequestId = HttpContext.TraceIdentifier;
        return View();
    }

    // Fallback for any route that doesn't match a real page (see Program.cs UseStatusCodePagesWithReExecute).
    // Only 404s get the friendly "not found" screen - anything else falls through to a
    // generic message so real errors don't get mislabeled as a missing page.
    [HttpGet]
    [AllowAnonymous]
    public IActionResult StatusCode(int code)
    {
        if (code == 404) return View("NotFound");

        ViewBag.RequestId = HttpContext.TraceIdentifier;
        return View("Error");
    }

    private bool Can(string permission) => User.HasClaim(Permissions.ClaimType, permission);

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

public class ClientErrorReport
{
    public string? Message { get; set; }
    public string? Source { get; set; }
    public int Line { get; set; }
    public string? Url { get; set; }
}
