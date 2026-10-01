using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Checkouts where money moved but no order was created. Most are returned automatically
/// (PaymentRecoveryService); this is where staff finish the ones that weren't - typically a card refund
/// the gateway rejected. Gated by the existing Orders permission.
/// </summary>
[Authorize(Policy = Permissions.OrdersManage)]
public class PaymentIncidentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWalletService _wallet;
    private readonly IRewardsService _rewards;
    private readonly IPaymentService _payments;
    private readonly UserManager<ApplicationUser> _userManager;

    public PaymentIncidentsController(
        ApplicationDbContext context,
        IWalletService wallet,
        IRewardsService rewards,
        IPaymentService payments,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _wallet = wallet;
        _rewards = rewards;
        _payments = payments;
        _userManager = userManager;
    }

    // GET: /PaymentIncidents?showAll=false
    [HttpGet]
    public async Task<IActionResult> Index(bool showAll = false)
    {
        var query = _context.PaymentIncidents.AsNoTracking().Include(i => i.Customer).AsQueryable();
        if (!showAll)
            query = query.Where(i => i.Status == PaymentIncidentStatus.NeedsAttention);

        ViewBag.ShowAll = showAll;
        return View(await query.OrderByDescending(i => i.DateCreated).Take(200).ToListAsync());
    }

    // POST: /PaymentIncidents/Retry/5 - tries again to hand back whatever is still outstanding.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int id)
    {
        var incident = await _context.PaymentIncidents.FirstOrDefaultAsync(i => i.PaymentIncidentId == id);
        if (incident is null) return NotFound();
        if (incident.Status != PaymentIncidentStatus.NeedsAttention)
        {
            this.ToastWarning("This incident is already closed.");
            return RedirectToAction(nameof(Index));
        }

        var notes = new List<string>();

        if (!incident.WalletRestored && incident.WalletAmount > 0 && !string.IsNullOrEmpty(incident.CustomerId))
        {
            var r = await _wallet.RestoreDebitAsync(incident.CustomerId, incident.Reference);
            incident.WalletRestored = r.Success;
            if (!r.Success) notes.Add($"FixCash: {r.ErrorMessage}");
        }

        if (!incident.PointsRestored && incident.PointsRedeemed > 0 && !string.IsNullOrEmpty(incident.CustomerId))
        {
            var r = await _rewards.RestoreRedemptionAsync(incident.CustomerId, incident.Reference);
            incident.PointsRestored = r.Success;
            if (!r.Success) notes.Add($"Points: {r.ErrorMessage}");
        }

        if (!incident.CardRefunded && incident.CardAmount > 0)
        {
            var r = await _payments.RefundTransactionAsync(incident.Reference, incident.CardAmount);
            incident.CardRefunded = r.Success;
            if (!r.Success) notes.Add($"Card: {r.ErrorMessage}");
        }

        var staffId = _userManager.GetUserId(User);
        if (incident.WalletRestored && incident.PointsRestored && incident.CardRefunded)
        {
            incident.Status = PaymentIncidentStatus.Resolved;
            incident.ResolutionNote = "Everything returned on retry.";
            incident.ResolvedByUserId = staffId;
            incident.DateResolved = DateTime.UtcNow;
            this.ToastSuccess($"{incident.Reference}: everything has now been returned.");
        }
        else
        {
            this.ToastError($"{incident.Reference}: still outstanding - {string.Join("; ", notes)}");
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = staffId,
            Action = "PaymentIncidentRetried",
            Details = $"Retried returns for {incident.Reference}. Card refunded: {incident.CardRefunded}, FixCash restored: {incident.WalletRestored}, points restored: {incident.PointsRestored}."
        });
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // POST: /PaymentIncidents/Resolve/5 - closes an incident that was handled by hand (e.g. refunded in the Paystack dashboard).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(int id, string note)
    {
        var incident = await _context.PaymentIncidents.FirstOrDefaultAsync(i => i.PaymentIncidentId == id);
        if (incident is null) return NotFound();

        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length < 5)
        {
            this.ToastError("Add a short note saying how it was resolved (at least 5 characters).");
            return RedirectToAction(nameof(Index));
        }

        var staffId = _userManager.GetUserId(User);
        incident.Status = PaymentIncidentStatus.Resolved;
        incident.ResolutionNote = note.Trim().Length > 500 ? note.Trim()[..500] : note.Trim();
        incident.ResolvedByUserId = staffId;
        incident.DateResolved = DateTime.UtcNow;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = staffId,
            Action = "PaymentIncidentResolved",
            Details = $"Closed payment incident {incident.Reference}: {incident.ResolutionNote}"
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"{incident.Reference} marked as resolved.");
        return RedirectToAction(nameof(Index));
    }
}
