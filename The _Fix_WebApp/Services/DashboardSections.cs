namespace FashionFix.Web.Services;

/// <summary>
/// Which parts of the dashboard to compute. HomeController.Dashboard() builds this from the
/// current user's permission claims (Can(...)) and passes it to IDashboardService - so which
/// widgets a role sees is genuinely "configurable through roles and perms" the same way every
/// other screen in this app is, not a second hardcoded config system. Flip a permission on the
/// Roles screen, the corresponding section starts/stops being computed and shown.
/// </summary>
[Flags]
public enum DashboardSections
{
    None = 0,
    CoreKpis = 1 << 0,           // DashboardView
    Inventory = 1 << 1,          // ProductsManage
    Orders = 1 << 2,             // OrdersManage
    Returns = 1 << 3,            // ReturnsProcess
    SupplyChain = 1 << 4,        // SuppliersManage or PurchaseOrdersManage
    Approvals = 1 << 5,          // PurchaseOrdersApprove specifically - narrower than SupplyChain
    Reports = 1 << 6,            // ReportsView
    Storefront = 1 << 7,         // StorefrontManage
    Staff = 1 << 8,              // EmployeesManage
    AuditActivity = 1 << 9,      // AuditLogsView
    Shift = 1 << 10,             // PosUse (own shift) / ReportsView (full history)
}
