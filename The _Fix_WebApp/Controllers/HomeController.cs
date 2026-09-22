using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IDashboardService dashboardService,
        ILogger<HomeController> logger)
    {
        _context = context;
        _userManager = userManager;
        _dashboardService = dashboardService;
        _logger = logger;
    }

    // GET: / - the public storefront landing page. Anyone can browse it (anonymous visitors
    // included) - only staff (Administrator/Manager/Employee/Owner) get bounced straight to
    // their Dashboard instead, since they don't need the customer-facing view by default.
    // Logged-in Customers see the storefront here too (with their cart/wishlist state),
    // rather than being redirected away like they used to be when this route was the login form.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var isStaff = roles.Any(r => r is "Administrator" or "Manager" or "Employee" or "Owner");
                if (isStaff)
                    return RedirectToAction(nameof(Dashboard));
            }
        }

        var curated = await _context.FeaturedProducts
            .AsNoTracking()
            .Include(f => f.Product).ThenInclude(p => p.Variants)
            .Where(f => f.IsActive && f.Product.IsActive)
            .OrderBy(f => f.DisplayOrder)
            .Take(8)
            .ToListAsync();

        List<TrendingCardViewModel> trending;
        if (curated.Count > 0)
        {
            trending = curated.Select(f => new TrendingCardViewModel
            {
                Product = f.Product,
                DisplayTitle = f.OverrideTitle ?? f.Product.Name,
                DisplayImageUrl = f.OverrideImageUrl ?? f.Product.ImageUrl,
                DisplayBadge = f.OverrideBadge ?? f.Product.Badge
            }).ToList();
        }
        else
        {
            // No admin curation yet - fall back to an automatic pick so the section is never empty.
            var automatic = await _context.Products
                .AsNoTracking()
                .Include(p => p.Variants)
                .Where(p => p.IsActive && p.Variants.Any(v => v.IsActive && v.StockQuantity > 0))
                .OrderByDescending(p => p.ReviewCount)
                .ThenByDescending(p => p.AverageRating)
                .Take(4)
                .ToListAsync();

            trending = automatic.Select(p => new TrendingCardViewModel
            {
                Product = p,
                DisplayTitle = p.Name,
                DisplayImageUrl = p.ImageUrl,
                DisplayBadge = p.Badge
            }).ToList();
        }

        var departments = await _context.Departments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync();

        var siteSettings = await _context.SiteSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1)
            ?? new SiteSettings();

        return View(new StorefrontLandingViewModel
        {
            Trending = trending,
            Departments = departments,
            SiteSettings = siteSettings
        });
    }

    // GET: /Home/Dashboard - dashboard of business statistics (US-04, US-05).
    // Staff-only: customers get their own account area instead (see CustomerController).
    // Which sections get computed is driven entirely by the current user's permission claims -
    // this IS the "configurable through roles and perms" mechanism: adjust a role's
    // permissions on the Roles screen and the corresponding widgets appear/disappear here,
    // with no separate dashboard-config system to keep in sync.
    [HttpGet]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<IActionResult> Dashboard()
    {
        var sections = BuildSectionsForCurrentUser();
        var model = await _dashboardService.BuildAsync(sections, _userManager.GetUserId(User));

        var (eyebrow, title, subtitle) = BuildHeroForCurrentUser();
        ViewBag.HeroEyebrow = eyebrow;
        ViewBag.HeroTitle = title;
        ViewBag.HeroSubtitle = subtitle;

        model.MyPermissionLabels = Permissions.All
            .Where(kv => Can(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        // Low-stock notification (US-03): surfaces as a toast every time a staff member
        // lands on the dashboard while items are below threshold, on top of the table below.
        if (sections.HasFlag(DashboardSections.Inventory) && model.LowStockCount > 0)
        {
            var names = string.Join(", ", model.LowStockVariants.Take(3).Select(v => $"{v.Product.Name} ({v.Size}/{v.Color})"));
            var suffix = model.LowStockCount > 3 ? $" and {model.LowStockCount - 3} more" : "";
            this.ToastWarning($"Low stock: {names}{suffix}.");
        }

        return View(model);
    }

    // GET: /Home/DashboardData - polled client-side every 15-30s to refresh the dashboard's
    // numbers in place, the same "poll, don't push" pattern already used for courier tracking
    // (ICourierService.RefreshTrackingAsync) - no SignalR, no fake-live socket claims.
    [HttpGet]
    [Authorize(Policy = Permissions.DashboardView)]
    public async Task<IActionResult> DashboardData()
    {
        var sections = BuildSectionsForCurrentUser();
        var model = await _dashboardService.BuildAsync(sections, _userManager.GetUserId(User));

        return Json(new
        {
            todaysSales = model.TodaysSales,
            todaysOrderCount = model.TodaysOrderCount,
            monthToDateRevenue = model.MonthToDateRevenue,
            averageOrderValueToday = model.AverageOrderValueToday,
            lowStockCount = model.LowStockCount,
            ordersNeedingAction = model.OrdersNeedingAction,
            pendingApprovalCount = model.PendingApprovalCount,
            attentionItems = model.AttentionItems
        });
    }

    private DashboardSections BuildSectionsForCurrentUser()
    {
        var sections = DashboardSections.CoreKpis; // everyone who can reach this page gets the base KPIs

        if (Can(Permissions.ProductsManage)) sections |= DashboardSections.Inventory;
        if (Can(Permissions.OrdersManage)) sections |= DashboardSections.Orders;
        if (Can(Permissions.ReturnsProcess)) sections |= DashboardSections.Returns;
        if (Can(Permissions.SuppliersManage) || Can(Permissions.PurchaseOrdersManage)) sections |= DashboardSections.SupplyChain;
        if (Can(Permissions.PurchaseOrdersApprove)) sections |= DashboardSections.Approvals;
        if (Can(Permissions.ReportsView)) sections |= DashboardSections.Reports;
        if (Can(Permissions.StorefrontManage)) sections |= DashboardSections.Storefront;
        if (Can(Permissions.EmployeesManage)) sections |= DashboardSections.Staff;
        if (Can(Permissions.AuditLogsView)) sections |= DashboardSections.AuditActivity;
        if (Can(Permissions.PosUse) || Can(Permissions.ReportsView)) sections |= DashboardSections.Shift;
        if (Can(Permissions.RolesManage)) sections |= DashboardSections.AccessControl;

        return sections;
    }

    // Chooses which "console" framing to show - based on what the user can actually do, not a
    // hardcoded role-name check, so a custom role (Section 3.1 of the app's permission model)
    // still gets a sensible tier instead of falling through to nothing. Highest tier whose
    // condition matches wins.
    private (string Eyebrow, string Title, string Subtitle) BuildHeroForCurrentUser()
    {
        if (Can(Permissions.RolesManage) && Can(Permissions.EmployeesManage))
            return ("Enterprise Governance", "Administrator Command Console",
                "Staff access control, permission matrices, and full operational oversight across every part of the store.");

        if (Can(Permissions.ReportsView) && (Can(Permissions.SuppliersManage) || Can(Permissions.PurchaseOrdersApprove)))
            return ("Operations Core", "Store Manager Operations & BI Console",
                "Sales velocity, restock queues, till reconciliation, and purchase approvals in one view.");

        if (Can(Permissions.PosUse))
            return ("Operations Core", "Workstation Console",
                "Your shift, your queue, your numbers for the day.");

        return ("Operations Core", "Dashboard", "");
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
}

public class ClientErrorReport
{
    public string? Message { get; set; }
    public string? Source { get; set; }
    public int Line { get; set; }
    public string? Url { get; set; }
}
