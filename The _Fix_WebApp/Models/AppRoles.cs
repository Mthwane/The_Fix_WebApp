namespace FashionFix.Web.Models;

/// <summary>
/// Single source of truth for role names. Nothing else in the codebase should contain a
/// literal role string - controllers, views, and the JWT token service all reference these
/// constants instead, so a role can be renamed/added in exactly one place.
///
/// These are still string constants (C# attributes like [Authorize(Roles=...)] require a
/// compile-time constant), but roles themselves are NOT hardcoded into authorization logic -
/// the actual list of roles a user holds is looked up dynamically at login time via
/// UserManager.GetRolesAsync() and encoded as claims in the JWT. This class only avoids
/// typos/duplication of the *names*, it doesn't hardcode who has which role.
/// </summary>
public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string Customer = "Customer";
    public const string Owner = "Owner";

    /// <summary>Every role that exists in the system - used to seed the Role table on startup.</summary>
    public static readonly string[] All = { Administrator, Manager, Employee, Customer, Owner };

    /// <summary>Roles that count as "staff" (i.e. NOT a storefront customer).</summary>
    public static readonly string[] StaffRoles = { Administrator, Manager, Employee, Owner };

    /// <summary>Roles an Administrator is allowed to assign when creating/editing a staff account.</summary>
    public static readonly string[] AssignableStaffRoles = { Administrator, Manager, Employee, Owner };

    // --- Common role-group combinations, expressed as compile-time constants so they can be
    // used directly in [Authorize(Roles = AppRoles.CatalogueManagers)] attributes. ---
    public const string CatalogueManagers = Administrator + "," + Manager;
    public const string TillStaff = Administrator + "," + Manager + "," + Employee;
    public const string BackOffice = Administrator + "," + Manager + "," + Owner;
    public const string AnyStaff = Administrator + "," + Manager + "," + Employee + "," + Owner;
}
