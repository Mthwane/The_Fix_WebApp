using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Records important activities (logins, product updates, sales, employee actions).
/// Only accessible to administrators per the non-functional requirements.
/// </summary>
public class AuditLog
{
    [Key]
    public int AuditLogId { get; set; }

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty; // e.g. "Login", "ProductUpdated", "SaleProcessed"

    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
