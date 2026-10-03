using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using FashionFix.Web.Services.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>Staff side of the support ticket system: the queue, assignment, replying, and
/// escalation. The customer side (creating a ticket, replying to their own) lives in
/// SupportController.</summary>
[Authorize(Policy = Permissions.SupportTicketsManage)]
public class SupportTicketsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public SupportTicketsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /SupportTickets - the queue. "assignedToMe=true" is how a staff member finds their
    // own open cases; with no filters at all, everything shows (including other staff's
    // tickets) so nothing silently falls through the cracks.
    [HttpGet]
    public async Task<IActionResult> Index(TicketStatus? status, bool? assignedToMe, bool? unassigned)
    {
        var userId = _userManager.GetUserId(User);
        await TicketSupport.AutoCloseStaleAsync(_context);

        var query = _context.SupportTickets
            .AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.AssignedEmployee)
            .Include(t => t.Order)
            .AsQueryable();

        // Closed tickets live in the Ticket History tab, not the working queue.
        query = status.HasValue
            ? query.Where(t => t.Status == status.Value)
            : query.Where(t => t.Status != TicketStatus.Closed);
        if (assignedToMe == true) query = query.Where(t => t.AssignedEmployeeId == userId);
        if (unassigned == true) query = query.Where(t => t.AssignedEmployeeId == null);

        var tickets = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DateCreated)
            .ToListAsync();

        ViewBag.Status = status;
        ViewBag.AssignedToMe = assignedToMe;
        ViewBag.Unassigned = unassigned;
        return View(tickets);
    }

    // GET: /SupportTickets/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _context.SupportTickets
            .Include(t => t.Customer)
            .Include(t => t.AssignedEmployee)
            .Include(t => t.EscalatedToManager)
            .Include(t => t.Order)
            .Include(t => t.Messages).ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync(t => t.SupportTicketId == id);

        if (ticket is null) return NotFound();

        // Any staff member with SupportTicketsManage can be assigned a ticket - Employee,
        // Manager, Administrator all hold it (see Permissions.DefaultRolePermissions).
        var staffIds = (await _userManager.GetUsersInRoleAsync("Employee")).Select(u => u.Id)
            .Concat((await _userManager.GetUsersInRoleAsync("Manager")).Select(u => u.Id))
            .Concat((await _userManager.GetUsersInRoleAsync("Administrator")).Select(u => u.Id))
            .Distinct()
            .ToList();
        ViewBag.StaffOptions = await _context.Users.Where(u => staffIds.Contains(u.Id)).OrderBy(u => u.FullName).ToListAsync();
        ViewBag.ManagerOptions = await _userManager.GetUsersInRoleAsync("Manager");

        return View(ticket);
    }

    // POST: /SupportTickets/Reply/5 - staff reply; optionally changes status in the same action
    // so responding and, say, marking Resolved is one click instead of two.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string? message, TicketStatus? newStatus, List<IFormFile>? attachments, [FromServices] IImageStorage imageStorage)
    {
        var userId = _userManager.GetUserId(User)!;
        var ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.SupportTicketId == id);
        if (ticket is null) return NotFound();

        if (ticket.Status == TicketStatus.Closed)
        {
            this.ToastError("This conversation is closed. It can no longer be replied to.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var (attachmentUrls, skippedFiles) = await TicketSupport.UploadAttachmentsAsync(attachments, imageStorage);

        if (!string.IsNullOrWhiteSpace(message) || attachmentUrls is not null)
        {
            _context.TicketMessages.Add(new TicketMessage
            {
                SupportTicketId = id,
                SenderId = userId,
                SenderType = TicketSenderType.Employee,
                Body = string.IsNullOrWhiteSpace(message) ? "(image attached)" : message.Trim(),
                AttachmentUrls = attachmentUrls
            });
        }

        if (newStatus.HasValue && newStatus.Value != ticket.Status)
        {
            ticket.Status = newStatus.Value;
            if (newStatus.Value is TicketStatus.Resolved or TicketStatus.Closed) ticket.DateResolved = DateTime.UtcNow;
            _context.TicketMessages.Add(new TicketMessage
            {
                SupportTicketId = id,
                SenderType = TicketSenderType.System,
                Body = $"Status changed to {newStatus.Value}."
            });
        }

        // Replying implicitly claims an unassigned ticket - staff shouldn't have to remember a
        // separate "assign to me" click before they can respond.
        if (ticket.AssignedEmployeeId is null)
        {
            ticket.AssignedEmployeeId = userId;
            if (ticket.Status == TicketStatus.Open) ticket.Status = TicketStatus.InProgress;
        }

        _context.AuditLogs.Add(new AuditLog { UserId = userId, Action = "SupportTicketReplied", Details = $"Ticket #{id}" });
        await _context.SaveChangesAsync();

        // Closing locks the conversation and saves the transcript to Ticket History.
        if (newStatus == TicketStatus.Closed)
        {
            ticket.Status = TicketStatus.Resolved; // let CloseAsync perform the real transition and stamp the transcript
            await _context.SaveChangesAsync();
            await TicketSupport.CloseAsync(_context, id, "This conversation was closed by support.");
            this.ToastSuccess("Ticket closed and saved to Ticket History.");
            return RedirectToAction(nameof(History));
        }

        if (skippedFiles > 0) this.ToastWarning($"Reply sent, but {skippedFiles} image(s) could not be uploaded (use JPG, PNG or WebP under 5 MB).");
        else this.ToastSuccess("Reply sent.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // GET: /SupportTickets/History - closed conversations, searchable and filterable, each with a downloadable .txt.
    [HttpGet]
    public async Task<IActionResult> History(TicketCategory? category, string? search, DateTime? from, DateTime? to, string? employeeId)
    {
        await TicketSupport.AutoCloseStaleAsync(_context);

        var query = _context.SupportTickets.AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.AssignedEmployee)
            .Include(t => t.Order)
            .Where(t => t.Status == TicketStatus.Closed);

        if (category.HasValue) query = query.Where(t => t.Category == category.Value);
        if (!string.IsNullOrWhiteSpace(employeeId)) query = query.Where(t => t.AssignedEmployeeId == employeeId);
        if (from.HasValue) query = query.Where(t => (t.DateClosed ?? t.DateCreated) >= from.Value.Date);
        if (to.HasValue) query = query.Where(t => (t.DateClosed ?? t.DateCreated) < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.Subject.Contains(term)
                || (t.Customer != null && t.Customer.FullName.Contains(term))
                || (t.Order != null && t.Order.OrderNumber.Contains(term))
                || t.SupportTicketId.ToString() == term.TrimStart('#'));
        }

        var tickets = await query.OrderByDescending(t => t.DateClosed ?? t.DateCreated).Take(300).ToListAsync();

        var staffIds = (await _userManager.GetUsersInRoleAsync("Employee")).Select(u => u.Id)
            .Concat((await _userManager.GetUsersInRoleAsync("Manager")).Select(u => u.Id))
            .Concat((await _userManager.GetUsersInRoleAsync("Administrator")).Select(u => u.Id))
            .Distinct().ToList();
        ViewBag.StaffOptions = await _context.Users.AsNoTracking().Where(u => staffIds.Contains(u.Id)).OrderBy(u => u.FullName).ToListAsync();
        ViewBag.Category = category;
        ViewBag.Search = search;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.EmployeeId = employeeId;
        return View(tickets);
    }

    // GET: /SupportTickets/Transcript/5 - download a closed ticket's conversation.
    [HttpGet]
    public async Task<IActionResult> Transcript(int id)
    {
        var ticket = await _context.SupportTickets.AsNoTracking().FirstOrDefaultAsync(t => t.SupportTicketId == id);
        if (ticket is null || ticket.TranscriptText is null) return NotFound();

        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(ticket.TranscriptText)).ToArray(),
            "text/plain; charset=utf-8", $"fashionfix-ticket-{ticket.SupportTicketId}.txt");
    }

    // POST: /SupportTickets/Assign/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, string employeeId)
    {
        var ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.SupportTicketId == id);
        if (ticket is null) return NotFound();

        var employee = await _userManager.FindByIdAsync(employeeId);
        if (employee is null)
        {
            this.ToastError("That staff member could not be found.");
            return RedirectToAction(nameof(Details), new { id });
        }

        ticket.AssignedEmployeeId = employeeId;
        if (ticket.Status == TicketStatus.Open) ticket.Status = TicketStatus.InProgress;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "SupportTicketAssigned",
            Details = $"Ticket #{id} assigned to {employee.FullName}."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Assigned to {employee.FullName}.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /SupportTickets/Escalate/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Escalate(int id, string managerId)
    {
        var ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.SupportTicketId == id);
        if (ticket is null) return NotFound();

        var manager = await _userManager.FindByIdAsync(managerId);
        if (manager is null)
        {
            this.ToastError("That manager could not be found.");
            return RedirectToAction(nameof(Details), new { id });
        }

        ticket.EscalatedToManagerId = managerId;
        ticket.Status = TicketStatus.Escalated;
        ticket.Priority = TicketPriority.High;

        _context.TicketMessages.Add(new TicketMessage
        {
            SupportTicketId = id,
            SenderType = TicketSenderType.System,
            Body = $"Escalated to {manager.FullName}."
        });
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "SupportTicketEscalated",
            Details = $"Ticket #{id} escalated to {manager.FullName}."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Escalated to {manager.FullName}.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
