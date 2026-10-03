using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.ViewComponents;

/// <summary>
/// The one sidebar used site-wide. Items are filtered by the signed-in user's permission claims
/// (staff) or role (customer account suite), so a custom role built on the Roles screen sees
/// exactly the links it was granted - no code change needed.
/// </summary>
public class SideNavViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public SideNavViewComponent(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync(string mode)
    {
        var principal = UserClaimsPrincipal;
        var user = await _userManager.GetUserAsync(principal);

        var model = new SideNavViewModel
        {
            Mode = mode,
            CurrentController = ViewContext.RouteData.Values["controller"]?.ToString() ?? string.Empty,
            CurrentAction = ViewContext.RouteData.Values["action"]?.ToString() ?? string.Empty,
            UserName = user?.FullName ?? string.Empty,
            Initials = Initials(user?.FullName)
        };

        var all = mode == "account" ? AccountSections() : StaffSections();
        model.BrandTitle = mode == "account" ? "Account Suite" : "FashionFix";
        model.BrandSubtitle = mode == "account" ? "Client Portfolio" : "Staff Operations";
        model.UserSubtitle = mode == "account"
            ? "Member"
            : (user?.JobPosition ?? string.Empty);

        // Gate every item by permission claim / role, then drop sections that ended up empty.
        bool Allowed(SideNavItem i) =>
            (i.Permission is null || principal.HasClaim(Permissions.ClaimType, i.Permission)) &&
            (i.Role is null || principal.IsInRole(i.Role));

        model.Sections = all
            .Select(s => new SideNavSection(s.Title, s.Items.Where(Allowed).ToList()))
            .Where(s => s.Items.Count > 0)
            .ToList();

        if (mode == "account" && user is not null)
            model.Badges = await AccountBadgesAsync(user.Id);

        return View(model);
    }

    private static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    private async Task<Dictionary<string, string>> AccountBadgesAsync(string userId)
    {
        var badges = new Dictionary<string, string>();

        // "Active" = placed but not yet delivered / closed out.
        var active = await _context.Orders.AsNoTracking()
            .CountAsync(o => o.CustomerId == userId &&
                             (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Processing || o.Status == OrderStatus.Shipped));
        if (active > 0) badges["orders"] = $"{active} active";

        var balance = await _context.WalletAccounts.AsNoTracking()
            .Where(w => w.CustomerId == userId)
            .Select(w => (decimal?)w.Balance)
            .FirstOrDefaultAsync();
        if (balance is > 0) badges["wallet"] = $"R{balance.Value:N0}";

        var wishlist = await _context.WishlistItems.AsNoTracking().CountAsync(w => w.CustomerId == userId);
        if (wishlist > 0) badges["wishlist"] = wishlist.ToString();

        return badges;
    }

    // ---------------------------------------------------------------- customer account suite
    private static List<SideNavSection> AccountSections() => new()
    {
        new SideNavSection(null, new List<SideNavItem>
        {
            new("Profile Details",   "badge",                 "Customer", "Profile",  null, "Customer", null, new[] { "Profile", "AddAddress", "EditAddress", "SaveAddress" }),
            new("Orders",            "shopping_bag",          "Customer", "Orders",   null, "Customer", "orders", new[] { "Orders", "Track" }),
            new("Wallet & Rewards",  "account_balance_wallet","Wallet",   "Index",    null, "Customer", "wallet"),
            new("FixRewards",        "redeem",                "Rewards",  "Index",    null, "Customer"),
            new("Wishlist",          "favorite",              "Customer", "Wishlist", null, "Customer", "wishlist", new[] { "Wishlist", "ToggleWishlist" }),
            new("Support Tickets",   "support_agent",         "Support",  "Tickets",  null, "Customer", null, new[] { "Tickets", "NewTicket", "TicketDetails" }),
            new("Help & FAQ",        "help",                  "Support",  "Faq"),
        }),
        // Staff who also use the storefront get a way back to their portal.
        new SideNavSection("Staff", new List<SideNavItem>
        {
            new("Staff Portal", "dashboard", "Home", "Dashboard", Permissions.DashboardView),
        }),
    };

    // ---------------------------------------------------------------- staff portal
    private static List<SideNavSection> StaffSections() => new()
    {
        new SideNavSection("Operations Core", new List<SideNavItem>
        {
            new("Dashboard",              "dashboard",            "Home",             "Dashboard", Permissions.DashboardView),
            new("Staff & Employees",      "groups",               "Employees",        "Index",     Permissions.EmployeesManage, null, null, null, new[] { "AuditLogs", "ExportAuditLogs" }),
            new("Roles & Permissions",    "admin_panel_settings", "Roles",            "Index",     Permissions.RolesManage),
            new("Audit Logs & Trail",     "policy",               "Employees",        "AuditLogs", Permissions.AuditLogsView, null, null, new[] { "AuditLogs", "ExportAuditLogs" }),
            new("Product Catalogue",      "inventory_2",          "Products",         "Index",     Permissions.ProductsManage),
            new("Pricing",                "sell",                 "Pricing",          "Index",     Permissions.ProductsManage),
            new("POS Till Workstation",   "point_of_sale",        "Pos",              "Index",     Permissions.PosUse),
            new("Orders & Dispatches",    "local_shipping",       "Orders",           "Index",     Permissions.OrdersManage),
            new("Payment Incidents",      "report",               "PaymentIncidents", "Index",     Permissions.OrdersManage),
            new("Departments",            "category",             "Departments",      "Index",     Permissions.StorefrontManage),
            new("Trending Picks",         "trending_up",          "Storefront",       "Trending",  Permissions.StorefrontManage, null, null, new[] { "Trending" }),
            new("Homepage Content",       "web",                  "Storefront",       "Content",   Permissions.StorefrontManage, null, null, new[] { "Content" }),
            new("Reviews",                "reviews",              "Storefront",       "Reviews",   Permissions.StorefrontManage, null, null, new[] { "Reviews" }),
            new("FAQs",                   "help",                 "Faqs",             "Index",     Permissions.StorefrontManage),
            new("Support Tickets",        "support_agent",        "SupportTickets",   "Index",     Permissions.SupportTicketsManage),
            new("Rewards",                "redeem",               "RewardsAdmin",     "Index",     Permissions.RewardsManage),
            new("FixCash Wallets",        "account_balance_wallet","WalletAdmin",     "Index",     Permissions.WalletView),
        }),
        new SideNavSection("Supply Chain", new List<SideNavItem>
        {
            new("Suppliers",              "warehouse",            "Suppliers",        "Index",     Permissions.SuppliersManage),
            new("Purchase Orders",        "receipt_long",         "PurchaseOrders",   "Index",     Permissions.PurchaseOrdersManage),
            new("Restock Bundles",        "inventory",            "RestockBundles",   "Index",     Permissions.PurchaseOrdersManage),
            new("Returns",                "assignment_return",    "Returns",          "Index",     Permissions.ReturnsProcess),
        }),
        new SideNavSection("Insight", new List<SideNavItem>
        {
            new("Reports & Margins",      "monitoring",           "Reports",          "Index",     Permissions.ReportsView),
        }),
    };
}
