using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

public enum TicketSenderType
{
    Customer,
    Employee,
    /// <summary>An auto-generated line (e.g. "Ticket escalated to a manager.") rather than
    /// something a person typed - rendered without a name/avatar in the thread.</summary>
    System
}

/// <summary>One message in a SupportTicket's thread. Plain request/response for now (posted via
/// a form, thread re-fetched on reload) - the planned live-chat layer reuses this exact table,
/// just pushed over SignalR instead of read back on the next page load.</summary>
public class TicketMessage
{
    [Key]
    public int TicketMessageId { get; set; }

    public int SupportTicketId { get; set; }
    public SupportTicket? SupportTicket { get; set; }

    /// <summary>Null for a System message; otherwise the customer or staff member who sent it.</summary>
    public string? SenderId { get; set; }
    public ApplicationUser? Sender { get; set; }

    public TicketSenderType SenderType { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public DateTime DateSent { get; set; } = DateTime.UtcNow;

    /// <summary>Up to 3 image URLs attached to this message, separated by '|'. Null when none.</summary>
    [MaxLength(1500)]
    public string? AttachmentUrls { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyList<string> Attachments =>
        string.IsNullOrWhiteSpace(AttachmentUrls)
            ? Array.Empty<string>()
            : AttachmentUrls.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
