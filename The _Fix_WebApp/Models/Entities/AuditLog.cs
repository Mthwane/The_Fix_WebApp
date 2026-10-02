using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Records important activities (logins, product updates, sales, employee actions).
/// Only accessible to administrators per the non-functional requirements.
///
/// Rows are append-only and hash-chained: every new row stores the hash of the row before it
/// (PreviousHash) and its own SHA-256 (Hash) over its contents. Editing or deleting a past row
/// breaks the chain, which the "Verify ledger" check on the Audit Logs screen detects.
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

    /// <summary>Client IP of the request that caused the entry (null for background jobs).</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>Hash of the previous sealed row (null for the first sealed row).</summary>
    [MaxLength(64)]
    public string? PreviousHash { get; set; }

    /// <summary>SHA-256 (hex) of this row's contents plus PreviousHash. Null on rows written before the ledger existed.</summary>
    [MaxLength(64)]
    public string? Hash { get; set; }
}
