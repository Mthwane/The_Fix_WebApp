namespace FashionFix.Web.Security;

/// <summary>
/// The full catalog of permissions in the system. This is the ONLY place a capability is
/// named as a string. Controllers authorize against these constants via policies
/// (see Program.cs), never against role names directly - roles are just named bundles of
/// these permissions that an Administrator assembles at runtime via the Roles screen.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    // Product Management
    public const string ProductsManage = "products.manage";

    // Storefront Management (departments, trending curation, homepage content, review moderation)
    public const string StorefrontManage = "storefront.manage";

    // Inventory / Sales
    public const string PosUse = "pos.use";

    // People
    public const string EmployeesManage = "employees.manage";
    public const string RolesManage = "roles.manage";

    // Fulfillment
    public const string OrdersManage = "orders.manage";

    // Supply Chain
    public const string SuppliersManage = "suppliers.manage";
    public const string PurchaseOrdersManage = "purchaseorders.manage";
    public const string PurchaseOrdersApprove = "purchaseorders.approve";
    public const string ReturnsProcess = "returns.process";

    // Insight
    public const string DashboardView = "dashboard.view";
    public const string ReportsView = "reports.view";
    public const string AuditLogsView = "auditlogs.view";

    // Support
    public const string SupportTicketsManage = "supporttickets.manage";
    public const string RewardsManage = "rewards.manage";

    // FixCash wallets (staff side)
    public const string WalletView = "wallet.view";
    public const string WalletAdjust = "wallet.adjust";

    // Discounts
    public const string DiscountsView = "discounts.view";     // see the discount list & use codes at the till
    public const string DiscountsManage = "discounts.manage"; // create / generate / edit / disable / storefront banner

    /// <summary>Every permission in the system, with a human-readable label for the Roles UI.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [ProductsManage] = "Manage Products (add/edit/deactivate catalogue items)",
        [StorefrontManage] = "Manage Storefront (departments, trending picks, homepage content, review moderation)",
        [PosUse] = "Use Point of Sale (process sales & print receipts)",
        [EmployeesManage] = "Manage Employees (create/edit/deactivate staff)",
        [RolesManage] = "Manage Roles & Permissions",
        [OrdersManage] = "Manage Orders (update status, cancel, fulfill)",
        [SuppliersManage] = "Manage Suppliers (contacts, lead times, collection addresses)",
        [PurchaseOrdersManage] = "Manage Purchase Orders (raise restock requests & receive stock)",
        [PurchaseOrdersApprove] = "Approve Purchase Orders (authorise spend before an order is placed)",
        [RewardsManage] = "Manage Rewards (points earn rate, cash-back %, redemption rules)",
        [ReturnsProcess] = "Process Returns & Refunds",
        [DashboardView] = "View Business Dashboard",
        [ReportsView] = "View Reports & Analytics",
        [AuditLogsView] = "View Audit Logs",
        [SupportTicketsManage] = "Manage Support Tickets (respond, assign, escalate)",
        [WalletView] = "View FixCash Wallets (balances, history, liability report)",
        [WalletAdjust] = "Adjust FixCash Wallets (manual credits and debits)",
        [DiscountsView] = "View & Use Discounts (see active discounts and apply codes at the till)",
        [DiscountsManage] = "Manage Discounts (create, generate, customise, disable, storefront banner)",
    };

    /// <summary>
    /// Permissions that can NEVER be taken off the Administrator role. Roles &amp; Permissions is the one screen that
    /// lets an administrator get back anything else they've removed from themselves, so losing it would be
    /// unrecoverable without editing the database.
    /// </summary>
    public static readonly IReadOnlySet<string> AdministratorLocked = new HashSet<string> { RolesManage };

    /// <summary>
    /// Permissions added AFTER the original release. At startup each is granted once to the built-in roles that
    /// list it in DefaultRolePermissions (so existing databases pick up the new screens), and a marker claim
    /// records that it was granted - so if an administrator later removes it from a role, it stays removed.
    /// </summary>
    public static readonly IReadOnlySet<string> IntroducedLater = new HashSet<string> { DiscountsView, DiscountsManage };

    /// <summary>Role-claim type used for the "this default was already offered to this role" marker.</summary>
    public const string SeededMarkerClaimType = "permission.seeded";

    /// <summary>Default permission bundles seeded for the built-in roles the first time each role is created.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultRolePermissions = new Dictionary<string, string[]>
    {
        ["Administrator"] = All.Keys.ToArray(), // everything
        // Manager can raise AND approve purchase orders - they hold the restock budget.
        ["Manager"] = new[] { ProductsManage, StorefrontManage, PosUse, DashboardView, ReportsView, OrdersManage, SuppliersManage, PurchaseOrdersManage, PurchaseOrdersApprove, ReturnsProcess, SupportTicketsManage, WalletView, WalletAdjust, DiscountsView, DiscountsManage },
        // Employee can raise a restock request and process returns at the till, but NOT approve
        // spend - that's the whole point of the approval gate.
        ["Employee"] = new[] { PosUse, DashboardView, OrdersManage, PurchaseOrdersManage, ReturnsProcess, SupportTicketsManage, WalletView, DiscountsView },
        ["Owner"] = new[] { DashboardView, ReportsView, WalletView },
        ["Customer"] = Array.Empty<string>(), // customers use the self-service area, not permission-gated staff screens
    };
}
