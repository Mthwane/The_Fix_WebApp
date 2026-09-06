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
        [ReturnsProcess] = "Process Returns & Refunds",
        [DashboardView] = "View Business Dashboard",
        [ReportsView] = "View Reports & Analytics",
        [AuditLogsView] = "View Audit Logs",
    };

    /// <summary>Default permission bundles seeded for the built-in roles the first time each role is created.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultRolePermissions = new Dictionary<string, string[]>
    {
        ["Administrator"] = All.Keys.ToArray(), // everything
        // Manager can raise AND approve purchase orders - they hold the restock budget.
        ["Manager"] = new[] { ProductsManage, StorefrontManage, PosUse, DashboardView, ReportsView, OrdersManage, SuppliersManage, PurchaseOrdersManage, PurchaseOrdersApprove, ReturnsProcess },
        // Employee can raise a restock request and process returns at the till, but NOT approve
        // spend - that's the whole point of the approval gate.
        ["Employee"] = new[] { PosUse, DashboardView, OrdersManage, PurchaseOrdersManage, ReturnsProcess },
        ["Owner"] = new[] { DashboardView, ReportsView },
        ["Customer"] = Array.Empty<string>(), // customers use the self-service area, not permission-gated staff screens
    };
}
