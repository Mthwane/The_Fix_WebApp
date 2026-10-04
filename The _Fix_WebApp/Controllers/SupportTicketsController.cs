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

    // POST: /SupportTickets/QuickAction/5 - the "Quick actions" dropdown on a ticket. One endpoint, one action name
    // per menu entry. Anything that moves money or cancels an order re-checks the matching permission here (the page
    // only needs Support Tickets access), and every outcome is written into the conversation as a system line so the
    // thread - and the closed-ticket transcript - shows exactly what support did.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAction(
        int id, string? action, decimal? amount, string? note,
        [FromServices] IOrderCancellationService cancellation,
        [FromServices] IWalletService wallet,
        [FromServices] IAuthorizationService authorization)
    {
        var userId = _userManager.GetUserId(User)!;
        var ticket = await _context.SupportTickets.Include(t => t.Order).FirstOrDefaultAsync(t => t.SupportTicketId == id);
        if (ticket is null) return NotFound();

        if (ticket.Status == TicketStatus.Closed)
        {
            this.ToastError("This conversation is closed. It can no longer be actioned.");
            return RedirectToAction(nameof(Details), new { id });
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var staff = await _userManager.GetUserAsync(User);
        var staffName = staff?.FullName ?? "Support";

        void SystemLine(string text) => _context.TicketMessages.Add(new TicketMessage
        {
            SupportTicketId = id,
            SenderType = TicketSenderType.System,
            Body = text
        });

        // Acting on a ticket claims it, same as replying does.
        void Claim()
        {
            if (ticket.AssignedEmployeeId is null) ticket.AssignedEmployeeId = userId;
            if (ticket.Status == TicketStatus.Open) ticket.Status = TicketStatus.InProgress;
        }

        switch (action)
        {
            case "cancel_order":
            {
                if (ticket.Order is null) { this.ToastError("This ticket isn't linked to an order."); break; }
                if (!(await authorization.AuthorizeAsync(User, Permissions.OrdersManage)).Succeeded)
                { this.ToastError("You need the Manage Orders permission to cancel an order."); break; }

                var result = await cancellation.CancelAsync(ticket.Order.OrderId, userId,
                    note ?? $"Cancelled from support ticket #{id}", $"support ticket #{id}");

                if (!result.Cancelled)
                {
                    this.ToastError(result.Message);
                    break;
                }

                Claim();
                SystemLine($"{staffName} cancelled order {ticket.Order.OrderNumber}. " + result.Message);
                if (ticket.Category is TicketCategory.CancelOrder or TicketCategory.RefundRequest)
                {
                    ticket.Status = TicketStatus.Resolved;
                    ticket.DateResolved = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();

                if (result.HasRefundProblem) this.ToastWarning(result.Message);
                else this.ToastSuccess(result.Message);
                break;
            }

            case "refund_wallet":
            {
                if (ticket.Order is null) { this.ToastError("This ticket isn't linked to an order."); break; }
                if (!(await authorization.AuthorizeAsync(User, Permissions.WalletAdjust)).Succeeded)
                { this.ToastError("You need the Adjust FixCash Wallets permission to issue a wallet refund."); break; }
                if (amount is null || amount <= 0)
                { this.ToastError("Enter the amount to refund."); break; }
                if (amount > ticket.Order.GrandTotal)
                { this.ToastError($"You can't refund more than the order total ({ticket.Order.GrandTotal:C})."); break; }

                var refunded = Math.Round(amount.Value, 2, MidpointRounding.AwayFromZero);
                var credit = await wallet.CreditRefundAsync(ticket.CustomerId, refunded, ticket.Order.OrderId,
                    $"Support refund for order {ticket.Order.OrderNumber} (ticket #{id})" + (note is null ? "" : $": {note}"));

                if (!credit.Success)
                {
                    this.ToastError($"The FixCash refund failed: {credit.ErrorMessage ?? "unknown error"}.");
                    break;
                }

                Claim();
                SystemLine($"{staffName} refunded {refunded:C} to the customer's FixCash wallet for order {ticket.Order.OrderNumber}.");
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "SupportWalletRefund",
                    Details = $"Ticket #{id}: {refunded:0.00} credited to FixCash for order {ticket.Order.OrderNumber}."
                });
                await _context.SaveChangesAsync();
                this.ToastSuccess($"{refunded:C} refunded to the customer's FixCash wallet.");
                break;
            }

            case "process_return":
                if (ticket.Order is null) { this.ToastError("This ticket isn't linked to an order."); break; }
                return RedirectToAction("Lookup", "Returns", new { orderNumber = ticket.Order.OrderNumber });

            case "request_info":
            {
                Claim();
                _context.TicketMessages.Add(new TicketMessage
                {
                    SupportTicketId = id,
                    SenderId = userId,
                    SenderType = TicketSenderType.Employee,
                    Body = note ??
                           "Hi, thanks for getting in touch. To look into this we need a little more detail - " +
                           "please reply with your order number, what went wrong, and a photo if that helps."
                });
                await _context.SaveChangesAsync();
                this.ToastSuccess("Request for more information sent.");
                break;
            }

            case "mark_resolved":
            {
                Claim();
                ticket.Status = TicketStatus.Resolved;
                ticket.DateResolved = DateTime.UtcNow;
                SystemLine($"{staffName} marked this ticket as resolved.");
                await _context.SaveChangesAsync();
                this.ToastSuccess("Ticket marked as resolved.");
                break;
            }

            case "close_ticket":
            {
                Claim();
                ticket.Status = TicketStatus.Resolved; // CloseAsync performs the real transition and stamps the transcript
                ticket.DateResolved = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await TicketSupport.CloseAsync(_context, id, "This conversation was closed by support.");
                this.ToastSuccess("Ticket closed and saved to Ticket History.");
                return RedirectToAction(nameof(History));
            }

            default:
                this.ToastError("Choose an action from the list first.");
                break;
        }

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
