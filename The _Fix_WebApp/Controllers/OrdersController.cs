using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using FashionFix.Web.Services.Courier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Where a placed order actually gets moved forward (US-08: "manage customer orders and
/// track deliveries"). A customer checkout only ever creates an order in Processing status -
/// nothing advances it automatically. Staff with the Manage Orders permission move it through
/// Processing -> Shipped -> Delivered here, or cancel it if it can't be fulfilled.
/// </summary>
[Authorize(Policy = Permissions.OrdersManage)]
public class OrdersController : Controller
{
    /// <summary>Statuses an order can still be cancelled from - once Delivered/Completed.</summary>
    private static readonly OrderStatus[] CancellableStatuses =
    {
        OrderStatus.Pending,
        OrderStatus.Processing,
        OrderStatus.Shipped
    };

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrdersController> _logger;
    private readonly IRewardsService _rewards;
    private readonly IWalletService _wallet;
    private readonly IPaymentService _payments;
    private readonly ICustomerNotificationService _notify;

    public OrdersController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        ILogger<OrdersController> logger,
        IRewardsService rewardsService,
        IWalletService wallet,
        IPaymentService payments,
        ICustomerNotificationService notify)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _userManager = userManager;
        _logger = logger;
        _rewards = rewardsService;
        _wallet = wallet;
        _payments = payments;
        _notify = notify;
    }

    // GET: /Orders?category=&status=&type=&search=
    [HttpGet]
    public async Task<IActionResult> Index(
        OrderCategory? category,
        OrderStatus? status,
        OrderType? type,
        string? search)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
            .AsQueryable();

        // Quick-filter tab takes priority over (and is mutually exclusive with) the
        // detailed status dropdown - picking a tab clears any single-status selection.
        if (category.HasValue)
        {
            var statuses = OrderCategorizer.StatusesFor[category.Value];
            query = query.Where(o => statuses.Contains(o.Status));
        }
        else if (status.HasValue)
        {
            query = query.Where(o => o.Status == status);
        }

        if (type.HasValue)
            query = query.Where(o => o.OrderType == type);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(o =>
                o.OrderNumber.Contains(term) ||
                (o.Customer != null && o.Customer.FullName.Contains(term)) ||
                (o.Customer != null &&
                 o.Customer.Email != null &&
                 o.Customer.Email.Contains(term)));
        }

        ViewBag.SelectedCategory = category;
        ViewBag.SelectedStatus = status;
        ViewBag.SelectedType = type;
        ViewBag.Search = search;

        // Counts for the tab badges - computed from the same base filters (type/search)
        // so the numbers stay accurate no matter what else the user has selected.
        var baseQuery = _context.Orders
            .AsNoTracking()
            .AsQueryable();

        if (type.HasValue)
            baseQuery = baseQuery.Where(o => o.OrderType == type);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            baseQuery = baseQuery.Where(o =>
                o.OrderNumber.Contains(term) ||
                (o.Customer != null && o.Customer.FullName.Contains(term)) ||
                (o.Customer != null &&
                 o.Customer.Email != null &&
                 o.Customer.Email.Contains(term)));
        }

        ViewBag.CategoryCounts = new Dictionary<OrderCategory, int>
        {
            [OrderCategory.Pending] = await baseQuery.CountAsync(
                o => OrderCategorizer.StatusesFor[OrderCategory.Pending].Contains(o.Status)),

            [OrderCategory.Completed] = await baseQuery.CountAsync(
                o => OrderCategorizer.StatusesFor[OrderCategory.Completed].Contains(o.Status)),

            [OrderCategory.Past] = await baseQuery.CountAsync(
                o => OrderCategorizer.StatusesFor[OrderCategory.Past].Contains(o.Status))
        };

        ViewBag.AllCount = await baseQuery.CountAsync();

        var orders = await query
            .OrderByDescending(o => o.DateCreated)
            .Take(200)
            .ToListAsync();

        return View(orders);
    }

    // GET: /Orders/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.ProcessedByUser)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductVariant)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null)
            return NotFound();

        // "Processed By": a till sale shows the cashier. An online order shows the customer who
        // raised a support ticket about it (an escalation), otherwise the courier handling delivery.
        string processedBy;
        if (order.OrderType == OrderType.POS && order.ProcessedByUser is not null)
        {
            processedBy = order.ProcessedByUser.FullName;
        }
        else
        {
            var ticket = await _context.SupportTickets.AsNoTracking()
                .Include(t => t.Customer)
                .Where(t => t.OrderId == order.OrderId)
                .OrderByDescending(t => t.DateCreated)
                .FirstOrDefaultAsync();
            processedBy = ticket?.Customer is not null
                ? $"{ticket.Customer.FullName} (raised ticket #{ticket.SupportTicketId})"
                : "The Courier Guy";
        }
        ViewBag.ProcessedByLabel = processedBy;

        return View(order);
    }

    // POST: /Orders/AdvanceStatus/5 - moves Online orders one step forward:
    // Processing -> Shipped -> Delivered.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStatus(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null)
            return NotFound();

        var next = order.Status switch
        {
            OrderStatus.Pending => OrderStatus.Processing,
            OrderStatus.Processing => OrderStatus.Shipped,
            OrderStatus.Shipped => OrderStatus.Delivered,
            _ => (OrderStatus?)null
        };

        if (next is null)
        {
            this.ToastError(
                $"Order {order.OrderNumber} is already {order.Status} - nothing further to advance.");

            return RedirectToAction(nameof(Index));
        }

        var previousStatus = order.Status;

        order.Status = next.Value;

        if (next == OrderStatus.Delivered)
            order.DateFulfilled = DateTime.UtcNow;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "OrderStatusChanged",
            Details = $"Order {order.OrderNumber}: {previousStatus} -> {order.Status}."
        });

        await _context.SaveChangesAsync();

        this.ToastSuccess(
            $"Order {order.OrderNumber} is now {order.Status}.");

        // Best-effort customer notification on each status change.
        if (order.Status == OrderStatus.Shipped)
        {
            await _notify.OrderDispatchedAsync(order.Customer, order);
        }
        else if (order.Status == OrderStatus.Delivered)
        {
            await _notify.OrderDeliveredAsync(order.Customer, order);
        }
        else if (order.Customer is not null &&
                 !string.IsNullOrWhiteSpace(order.Customer.Email))
        {
            await _emailSender.SendAsync(
                order.Customer.Email,
                $"Order {order.OrderNumber} update: {order.Status}",
                $"<p>Hi {WebUtility.HtmlEncode(order.Customer.FullName)},</p>" +
                $"<p>Your order <strong>{WebUtility.HtmlEncode(order.OrderNumber)}</strong> is now " +
                $"<strong>{order.Status}</strong>.</p>");
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: /Orders/Cancel/5 - staff-initiated cancellation (any role with Manage Orders). Restocks every item,
    // reverses the points, and returns the customer's money to the ORIGINAL payment (FixCash back to the wallet, card
    // back to the card). All of that lives in IOrderCancellationService so the support-ticket quick action behaves identically.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason, [FromServices] IOrderCancellationService cancellation)
    {
        var result = await cancellation.CancelAsync(id, _userManager.GetUserId(User), reason, "Orders screen");

        if (result.NotFound) return NotFound();

        if (!result.Cancelled) this.ToastError(result.Message);
        else if (result.HasRefundProblem) this.ToastWarning(result.Message);
        else this.ToastSuccess(result.Message);

        return RedirectToAction(nameof(Index));
    }

    // ===================== Courier delivery =====================

    // POST: /Orders/BookDelivery/5 - creates the waybill at The Courier Guy for an online order.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookDelivery(
        int id,
        [FromServices] ICourierService courier)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null)
            return NotFound();

        var result = await courier.CreateOrderShipmentAsync(order);

        if (!result.Success)
        {
            this.ToastError(result.ErrorMessage!);
            return RedirectToAction(nameof(Index));
        }

        // Booking the courier is what "shipped" actually means for an online order, so advance
        // the status here rather than making staff do it as a separate manual step.
        var becameShipped = order.Status is OrderStatus.Pending or OrderStatus.Processing;
        if (becameShipped)
            order.Status = OrderStatus.Shipped;

        await _context.SaveChangesAsync();

        if (becameShipped)
            await _notify.OrderDispatchedAsync(order.Customer, order, result.Data!.TrackingReference);

        this.ToastSuccess(
            $"Waybill {result.Data!.TrackingReference} created for {order.OrderNumber}.");

        return RedirectToAction(nameof(Index));
    }

    // POST: /Orders/RefreshTracking/5 - pull fresh tracking for one shipment on demand.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshTracking(
        int id,
        [FromServices] ICourierService courier)
    {
        var result = await courier.RefreshTrackingAsync(id, force: true);

        if (!result.Success)
            this.ToastError(result.ErrorMessage!);
        else
            this.ToastSuccess($"Tracking updated - {result.Data!.Stage}.");

        return RedirectToAction(nameof(Index));
    }

    // GET: /Orders/TrackingStatus - JSON for the dashboard's periodic poll. Returns the locally
    // cached status for every live shipment and only hits the courier for ones past the cache
    // window, so a page polling every 30s doesn't turn into 30s-interval API hammering.
    [HttpGet]
    public async Task<IActionResult> TrackingStatus(
        [FromServices] ICourierService courier)
    {
        var shipments = await _context.CourierShipments
            .Include(s => s.Order)
            .Where(s =>
                s.OrderId != null &&
                s.Status != "delivered" &&
                s.Status != "cancelled")
            .ToListAsync();

        foreach (var shipment in shipments)
        {
            await courier.RefreshTrackingAsync(
                shipment.CourierShipmentId);
        }

        return Json(shipments.Select(s => new
        {
            orderId = s.OrderId,
            orderNumber = s.Order?.OrderNumber,
            waybill = s.TrackingReference,
            status = s.Status,
            stage = s.Stage,
            lastSynced = s.LastSyncedAt
        }));
    }

    // GET: /Orders/Waybill/5 - redirects to the courier's signed PDF (expires after 24h).
    [HttpGet]
    public async Task<IActionResult> Waybill(
        int id,
        [FromServices] ICourierService courier)
    {
        var result = await courier.GetLabelUrlAsync(id);

        if (!result.Success)
        {
            this.ToastError(result.ErrorMessage!);
            return RedirectToAction(nameof(Index));
        }

        return Redirect(result.Data!);
    }
}