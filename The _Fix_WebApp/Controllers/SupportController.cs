using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Services;
using FashionFix.Web.Services.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Customer-facing support: the public FAQ page (no login needed) and a customer's own support
/// tickets (login required). The staff side of ticket handling lives in
/// SupportTicketsController.
/// </summary>
public class SupportController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<SupportController> _logger;

    public SupportController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        ILogger<SupportController> logger)
    {
        _context = context;
        _userManager = userManager;
        _emailSender = emailSender;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    // GET: /Support/Faq - public, no login required.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Faq()
    {
        var faqs = await _context.Faqs
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Category)
            .ThenBy(f => f.DisplayOrder)
            .ToListAsync();

        var grouped = faqs
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Min(f => f.DisplayOrder))
            .ToList();

        return View(grouped);
    }

    // GET: /Support/Tickets - the customer's own ticket list.
    [HttpGet]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Tickets(string? tab, TicketCategory? category, string? search)
    {
        var userId = _userManager.GetUserId(User);
        await TicketSupport.AutoCloseStaleAsync(_context);

        var history = string.Equals(tab, "history", StringComparison.OrdinalIgnoreCase);
        var query = _context.SupportTickets
            .AsNoTracking()
            .Where(t => t.CustomerId == userId)
            .Include(t => t.Order)
            .AsQueryable();

        query = history
            ? query.Where(t => t.Status == TicketStatus.Closed)
            : query.Where(t => t.Status != TicketStatus.Closed);

        if (category.HasValue) query = query.Where(t => t.Category == category.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.Subject.Contains(term) || (t.Order != null && t.Order.OrderNumber.Contains(term)));
        }

        var tickets = await query
            .OrderByDescending(t => history ? (t.DateClosed ?? t.DateCreated) : t.DateCreated)
            .ToListAsync();

        ViewBag.Tab = history ? "history" : "active";
        ViewBag.Category = category;
        ViewBag.Search = search;
        return View(tickets);
    }

    // GET: /Support/NewTicket?orderId=5 - orderId is optional; when given (e.g. from "Ask about
    // this order" on the order history page) it's pre-selected and locks the category options
    // down to ones that make sense for an order.
    [HttpGet]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> NewTicket(int? orderId)
    {
        var userId = _userManager.GetUserId(User);

        if (orderId.HasValue)
        {
            var owned = await _context.Orders.AnyAsync(o => o.OrderId == orderId && o.CustomerId == userId);
            if (!owned) return NotFound();
        }

        ViewBag.OrderId = orderId;
        return View();
    }

    // POST: /Support/NewTicket
    [HttpPost]
    [Authorize(Roles = "Customer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewTicket(TicketCategory category, string subject, string message, int? orderId, List<IFormFile>? attachments, [FromServices] IImageStorage imageStorage)
    {
        var userId = _userManager.GetUserId(User)!;

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
        {
            this.ToastError("Please fill in both a subject and a message.");
            ViewBag.OrderId = orderId;
            return View();
        }

        if (orderId.HasValue)
        {
            var owned = await _context.Orders.AnyAsync(o => o.OrderId == orderId && o.CustomerId == userId);
            if (!owned) return NotFound();
        }

        var (attachmentUrls, skippedFiles) = await TicketSupport.UploadAttachmentsAsync(attachments, imageStorage);

        var ticket = new SupportTicket
        {
            CustomerId = userId,
            Subject = subject.Trim(),
            Category = category,
            OrderId = orderId,
            Priority = category == TicketCategory.General ? TicketPriority.Low : TicketPriority.Normal
        };
        ticket.Messages.Add(new TicketMessage
        {
            SenderId = userId,
            SenderType = TicketSenderType.Customer,
            Body = message.Trim(),
            AttachmentUrls = attachmentUrls
        });

        _context.SupportTickets.Add(ticket);
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "SupportTicketCreated",
            Details = $"Ticket #{ticket.SupportTicketId} ({category}): {ticket.Subject}"
        });
        await _context.SaveChangesAsync();

        // Best-effort "ping" - a failed notification must never block ticket creation, same
        // principle IEmailSender itself is built around.
        var notifyTo = string.IsNullOrWhiteSpace(_emailOptions.SupportNotifyEmail) ? _emailOptions.FromAddress : _emailOptions.SupportNotifyEmail;
        try
        {
            await _emailSender.SendAsync(
                notifyTo,
                $"New support ticket #{ticket.SupportTicketId}: {ticket.Subject}",
                $"<p>New {category} ticket from a customer.</p><p><strong>{ticket.Subject}</strong></p><p>{System.Net.WebUtility.HtmlEncode(message)}</p>");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Support ticket notification email failed for ticket {TicketId}.", ticket.SupportTicketId);
        }

        if (skippedFiles > 0) this.ToastWarning($"Your ticket was submitted, but {skippedFiles} image(s) could not be uploaded (use JPG, PNG or WebP under 5 MB).");
        else this.ToastSuccess("Your ticket has been submitted - we'll get back to you shortly.");
        return RedirectToAction(nameof(TicketDetails), new { id = ticket.SupportTicketId });
    }

    // GET: /Support/TicketDetails/5
    [HttpGet]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> TicketDetails(int id)
    {
        var userId = _userManager.GetUserId(User);

        var ticket = await _context.SupportTickets
            .Include(t => t.Order)
            .Include(t => t.AssignedEmployee)
            .Include(t => t.Messages).ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync(t => t.SupportTicketId == id && t.CustomerId == userId);

        if (ticket is null) return NotFound();

        return View(ticket);
    }

    // POST: /Support/Reply/5 - customer adding to their own ticket thread.
    [HttpPost]
    [Authorize(Roles = "Customer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string? message, List<IFormFile>? attachments, [FromServices] IImageStorage imageStorage)
    {
        var userId = _userManager.GetUserId(User)!;
        var ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.SupportTicketId == id && t.CustomerId == userId);
        if (ticket is null) return NotFound();

        if (ticket.Status == TicketStatus.Closed)
        {
            this.ToastError("This ticket is closed. Please open a new ticket if you need further help.");
            return RedirectToAction(nameof(TicketDetails), new { id });
        }

        var (attachmentUrls, skippedFiles) = await TicketSupport.UploadAttachmentsAsync(attachments, imageStorage);

        if (string.IsNullOrWhiteSpace(message) && attachmentUrls is null)
        {
            this.ToastError("Message can't be empty.");
            return RedirectToAction(nameof(TicketDetails), new { id });
        }

        _context.TicketMessages.Add(new TicketMessage
        {
            SupportTicketId = id,
            SenderId = userId,
            SenderType = TicketSenderType.Customer,
            Body = string.IsNullOrWhiteSpace(message) ? "(image attached)" : message.Trim(),
            AttachmentUrls = attachmentUrls
        });

        // A customer replying to a Resolved ticket means it isn't actually resolved -
        // reopen it so it lands back on staff's radar rather than sitting silently closed.
        if (ticket.Status == TicketStatus.Resolved) ticket.Status = TicketStatus.Open;

        await _context.SaveChangesAsync();
        if (skippedFiles > 0) this.ToastWarning($"{skippedFiles} image(s) could not be uploaded (use JPG, PNG or WebP under 5 MB).");
        return RedirectToAction(nameof(TicketDetails), new { id });
    }

    // POST: /Support/Close/5 - the customer ends the conversation themselves ("my problem is sorted").
    [HttpPost]
    [Authorize(Roles = "Customer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id)
    {
        var userId = _userManager.GetUserId(User);
        var owned = await _context.SupportTickets.AnyAsync(t => t.SupportTicketId == id && t.CustomerId == userId);
        if (!owned) return NotFound();

        await TicketSupport.CloseAsync(_context, id, "The customer closed this conversation.");
        this.ToastSuccess("Ticket closed. You can download the conversation from Ticket History.");
        return RedirectToAction(nameof(Tickets), new { tab = "history" });
    }

    // GET: /Support/Transcript/5 - downloads the closed ticket's conversation as a .txt file.
    [HttpGet]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Transcript(int id)
    {
        var userId = _userManager.GetUserId(User);
        var ticket = await _context.SupportTickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.SupportTicketId == id && t.CustomerId == userId);
        if (ticket is null || ticket.TranscriptText is null) return NotFound();

        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(ticket.TranscriptText)).ToArray(),
            "text/plain; charset=utf-8", $"fashionfix-ticket-{ticket.SupportTicketId}.txt");
    }
}
