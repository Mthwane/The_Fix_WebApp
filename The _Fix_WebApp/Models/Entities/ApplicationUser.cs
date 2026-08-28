using Microsoft.AspNetCore.Identity;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Extends the default Identity user so the same table backs Store Owners,
/// Administrators, Managers, Employees AND Customers - differentiated by role.
/// Staff-only fields are nullable since they don't apply to Customer accounts.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    // --- Employee-specific fields (null for Customer accounts) ---
    public string? JobPosition { get; set; }
    public string? EmploymentStatus { get; set; } // e.g. Active, On Leave, Terminated
    public DateTime? DateHired { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    // --- Navigation ---
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();
    public ICollection<CustomerPaymentMethod> PaymentMethods { get; set; } = new List<CustomerPaymentMethod>();
}