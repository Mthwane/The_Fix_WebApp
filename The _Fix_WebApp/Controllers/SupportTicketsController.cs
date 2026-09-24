using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
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

        var query = _context.SupportTickets
            .AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.AssignedEmployee)
            .Include(t => t.Order)
            .AsQueryable();

        if (status.HasValue) query = query.Where(t => t.Status == status.Value);
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
    public async Task<IActionResult> Reply(int id, string message, TicketStatus? newStatus)
    {
        var userId = _userManager.GetUserId(User)!;
        var ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.SupportTicketId == id);
        if (ticket is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(message))
        {
            _context.TicketMessages.Add(new TicketMessage
            {
                SupportTicketId = id,
                SenderId = userId,
                SenderType = TicketSenderType.Employee,
                Body = message.Trim()
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

        this.ToastSuccess("Reply sent.");
        return RedirectToAction(nameof(Details), new { id });
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
