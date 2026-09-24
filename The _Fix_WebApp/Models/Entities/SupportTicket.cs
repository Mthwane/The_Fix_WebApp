using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

public enum TicketCategory
{
    General,
    OrderQuery,
    RefundRequest,
    CancelOrder
}

public enum TicketStatus
{
    Open,
    InProgress,
    Escalated,
    Resolved,
    Closed
}

public enum TicketPriority
{
    Low,
    Normal,
    High
}

/// <summary>
/// A customer support case (US-support-2). Doubles as the container for its own message thread
/// (see TicketMessage) - there's deliberately no separate "chat session" concept yet; a ticket
/// IS the conversation. Live/real-time messaging (SignalR) is a planned follow-up layered on top
/// of this same table, not a different one.
/// </summary>
public class SupportTicket
{
    [Key]
    public int SupportTicketId { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    [Required, MaxLength(150)]
    public string Subject { get; set; } = string.Empty;

    public TicketCategory Category { get; set; } = TicketCategory.General;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    /// <summary>Optional - set when the customer opened the ticket about a specific order
    /// (pre-selected for OrderQuery/RefundRequest/CancelOrder categories). Null for a General
    /// ticket.</summary>
    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>The staff member currently handling this ticket. Null while unassigned/in the
    /// open queue.</summary>
    public string? AssignedEmployeeId { get; set; }
    public ApplicationUser? AssignedEmployee { get; set; }

    /// <summary>Set when a staff member escalates the ticket to a manager - Status also moves
    /// to Escalated at the same time, and AssignedEmployeeId is left as-is (who escalated it),
    /// distinct from who it was escalated TO.</summary>
    public string? EscalatedToManagerId { get; set; }
    public ApplicationUser? EscalatedToManager { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? DateResolved { get; set; }

    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
}
