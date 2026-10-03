using System.Security.Claims;

namespace FashionFix.Web.Security;

/// <summary>
/// Where a signed-in staff member should land. Because an Administrator can now remove permissions from themselves,
/// "everyone goes to the Dashboard" is no longer safe - someone without dashboard.view would hit Access Denied
/// straight after logging in. Roles &amp; Permissions can never be removed from the Administrator, so there is always
/// somewhere to land.
/// </summary>
public static class StaffLanding
{
    public static (string Controller, string Action) For(ClaimsPrincipal principal)
    {
        bool Has(string p) => principal.HasClaim(Permissions.ClaimType, p);

        if (Has(Permissions.DashboardView)) return ("Home", "Dashboard");
        if (Has(Permissions.PosUse)) return ("Pos", "Index");
        if (Has(Permissions.PurchaseOrdersManage)) return ("PurchaseOrders", "Index");
        if (Has(Permissions.OrdersManage)) return ("Orders", "Index");
        if (Has(Permissions.ProductsManage)) return ("Products", "Index");
        if (Has(Permissions.DiscountsView)) return ("Discounts", "Index");
        if (Has(Permissions.RolesManage)) return ("Roles", "Index");
        return ("Account", "ChangePassword");
    }
}
