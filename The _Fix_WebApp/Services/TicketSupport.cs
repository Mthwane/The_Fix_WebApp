using System.Text;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using FashionFix.Web.Services.Images;

namespace FashionFix.Web.Services;

/// <summary>Helpers shared by the customer and staff support controllers.</summary>
public static class TicketSupport
{
    public const int MaxAttachments = 3;

    /// <summary>Uploads up to three images and returns the '|'-joined URLs (null when none). Bad files are counted in skipped.</summary>
    public static async Task<(string? Urls, int Skipped)> UploadAttachmentsAsync(
        IEnumerable<IFormFile>? files, IImageStorage storage, string folder = "support")
    {
        var urls = new List<string>();
        var skipped = 0;
        foreach (var file in (files ?? Enumerable.Empty<IFormFile>()).Where(f => f.Length > 0).Take(MaxAttachments))
        {
            var result = await storage.UploadAsync(file, folder);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Url)) urls.Add(result.Url!);
            else skipped++;
        }
        return (urls.Count > 0 ? string.Join("|", urls) : null, skipped);
    }

    /// <summary>
    /// Closes a ticket: locks the conversation, stamps DateClosed, appends a system line, and saves a plain-text
    /// transcript onto the ticket so it appears in the Ticket History tab. Safe to call on an already-closed ticket (no-op).
    /// </summary>
    public static async Task CloseAsync(ApplicationDbContext context, int ticketId, string reason)
    {
        var ticket = await context.SupportTickets
            .Include(t => t.Customer)
            .Include(t => t.AssignedEmployee)
            .Include(t => t.Order)
            .Include(t => t.Messages).ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync(t => t.SupportTicketId == ticketId);
        if (ticket is null || ticket.Status == TicketStatus.Closed && ticket.TranscriptText is not null) return;

        ticket.Status = TicketStatus.Closed;
        ticket.DateClosed = DateTime.UtcNow;
        ticket.DateResolved ??= ticket.DateClosed;

        var line = new TicketMessage
        {
            SupportTicketId = ticket.SupportTicketId,
            SenderType = TicketSenderType.System,
            Body = reason,
            DateSent = DateTime.UtcNow
        };
        context.TicketMessages.Add(line);
        ticket.Messages.Add(line);

        ticket.TranscriptText = BuildTranscript(ticket);
        await context.SaveChangesAsync();
    }

    /// <summary>Closes tickets that have sat in Resolved for more than the given number of days with no activity.</summary>
    public static async Task AutoCloseStaleAsync(ApplicationDbContext context, int days = 3)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var staleIds = await context.SupportTickets.AsNoTracking()
            .Where(t => t.Status == TicketStatus.Resolved && t.DateResolved != null && t.DateResolved < cutoff)
            .Where(t => !t.Messages.Any(m => m.DateSent > cutoff))
            .Select(t => t.SupportTicketId)
            .ToListAsync();
        foreach (var id in staleIds)
            await CloseAsync(context, id, $"Closed automatically after {days} days with no further activity.");
    }

    /// <summary>Builds the plain-text transcript. The ticket must have Customer, AssignedEmployee, Order and Messages(+Sender) loaded.</summary>
    public static string BuildTranscript(SupportTicket t)
    {
        var za = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "South Africa Standard Time" : "Africa/Johannesburg");
        string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), za).ToString("yyyy-MM-dd HH:mm");

        var sb = new StringBuilder();
        sb.AppendLine("FASHION FIX - SUPPORT TICKET TRANSCRIPT");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Ticket:    #{t.SupportTicketId}");
        sb.AppendLine($"Subject:   {t.Subject}");
        sb.AppendLine($"Category:  {t.Category}");
        sb.AppendLine($"Customer:  {t.Customer?.FullName ?? t.CustomerId}");
        sb.AppendLine($"Handled by: {t.AssignedEmployee?.FullName ?? "Unassigned"}");
        if (t.Order is not null) sb.AppendLine($"Order:     {t.Order.OrderNumber}");
        sb.AppendLine($"Opened:    {Local(t.DateCreated)}");
        sb.AppendLine($"Closed:    {(t.DateClosed.HasValue ? Local(t.DateClosed.Value) : "-")}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (var m in t.Messages.OrderBy(m => m.DateSent))
        {
            var who = m.SenderType switch
            {
                TicketSenderType.System => "System",
                TicketSenderType.Customer => t.Customer?.FullName ?? "Customer",
                _ => m.Sender?.FullName ?? "Fashion Fix Support"
            };
            sb.AppendLine($"[{Local(m.DateSent)}] {who}:");
            sb.AppendLine(m.Body);
            foreach (var a in m.Attachments) sb.AppendLine($"  (attachment: {a})");
            sb.AppendLine();
        }

        sb.AppendLine(new string('-', 60));
        sb.AppendLine("End of conversation. This ticket is closed.");
        return sb.ToString();
    }
}
