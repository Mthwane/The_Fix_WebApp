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

    // Inventory / Sales
    public const string PosUse = "pos.use";
    public const string ReturnsProcess = "returns.process";

    // Purchasing
    public const string PurchaseOrdersManage = "purchaseorders.manage";
    public const string SuppliersManage = "suppliers.manage";

    // People
    public const string EmployeesManage = "employees.manage";
    public const string RolesManage = "roles.manage";

    // Fulfillment
    public const string OrdersManage = "orders.manage";

    // Insight
    public const string DashboardView = "dashboard.view";
    public const string ReportsView = "reports.view";
    public const string AuditLogsView = "auditlogs.view";

    /// <summary>Every permission in the system, with a human-readable label for the Roles UI.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [ProductsManage] = "Manage Products (add/edit/deactivate catalogue items)",
        [PosUse] = "Use Point of Sale (process sales & print receipts)",
        [ReturnsProcess] = "Process Returns & Exchanges",
        [PurchaseOrdersManage] = "Manage Purchase Orders",
        [SuppliersManage] = "Manage Suppliers",
        [EmployeesManage] = "Manage Employees (create/edit/deactivate staff)",
        [RolesManage] = "Manage Roles & Permissions",
        [OrdersManage] = "Manage Orders (update status, cancel, fulfill)",
        [DashboardView] = "View Business Dashboard",
        [ReportsView] = "View Reports & Analytics",
        [AuditLogsView] = "View Audit Logs",
    };

    /// <summary>Default permission bundles seeded for the built-in roles the first time each role is created.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultRolePermissions = new Dictionary<string, string[]>
    {
        ["Administrator"] = All.Keys.ToArray(), // everything
        ["Manager"] = new[] { ProductsManage, PosUse, ReturnsProcess, PurchaseOrdersManage, SuppliersManage, DashboardView, ReportsView, OrdersManage },
        ["Employee"] = new[] { PosUse, ReturnsProcess, DashboardView, OrdersManage },
        ["Owner"] = new[] { DashboardView, ReportsView, PurchaseOrdersManage, SuppliersManage },
        ["Customer"] = Array.Empty<string>(), // customers use the self-service area, not permission-gated staff screens
    };
}
