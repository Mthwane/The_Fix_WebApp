using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Where a placed order actually gets moved forward (US-08: "manage customer orders and
/// track deliveries"). A customer checkout only ever creates an order in Processing status -
/// nothing advances it automatically. Staff with the Manage Orders permission move it through
/// Processing -&gt; Shipped -&gt; Delivered here, or cancel it if it can't be fulfilled.
/// </summary>
[Authorize(Policy = Permissions.OrdersManage)]
public class OrdersController : Controller
{
    /// <summary>Statuses an order can still be cancelled from - once Delivered/Completed.</summary>
    private static readonly OrderStatus[] CancellableStatuses = { OrderStatus.Pending, OrderStatus.Processing, OrderStatus.Shipped };

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Orders?status=&type=
    [HttpGet]
    public async Task<IActionResult> Index(OrderStatus? status, OrderType? type)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
            .AsQueryable();

        if (status.HasValue) query = query.Where(o => o.Status == status);
        if (type.HasValue) query = query.Where(o => o.OrderType == type);

        ViewBag.SelectedStatus = status;
        ViewBag.SelectedType = type;

        var orders = await query.OrderByDescending(o => o.DateCreated).Take(200).ToListAsync();
        return View(orders);
    }

    // GET: /Orders/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.ProcessedByUser)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null) return NotFound();
        return View(order);
    }

    // POST: /Orders/AdvanceStatus/5 - moves Online orders one step forward:
    // Processing -> Shipped -> Delivered.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStatus(int id)
    {
        var order = await _context.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.OrderId == id);
        if (order is null) return NotFound();

        var next = order.Status switch
        {
            OrderStatus.Pending => OrderStatus.Processing,
            OrderStatus.Processing => OrderStatus.Shipped,
            OrderStatus.Shipped => OrderStatus.Delivered,
            _ => (OrderStatus?)null
        };

        if (next is null)
        {
            this.ToastError($"Order {order.OrderNumber} is already {order.Status} - nothing further to advance.");
            return RedirectToAction(nameof(Index));
        }

        var previousStatus = order.Status;
        order.Status = next.Value;
        if (next == OrderStatus.Delivered) order.DateFulfilled = DateTime.UtcNow;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "OrderStatusChanged",
            Details = $"Order {order.OrderNumber}: {previousStatus} -> {order.Status}."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Order {order.OrderNumber} is now {order.Status}.");
        

        // Best-effort customer notification on each status change.
        if (order.Customer is not null && !string.IsNullOrWhiteSpace(order.Customer.Email))
        {
            await _emailSender.SendAsync(
                order.Customer.Email,
                $"Order {order.OrderNumber} update: {order.Status}",
                $"<p>Hi {order.Customer.FullName},</p><p>Your order <strong>{order.OrderNumber}</strong> is now <strong>{order.Status}</strong>.</p>");
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: /Orders/Cancel/5 - staff-initiated cancellation (any role with Manage Orders),
    // restocks every item on the order. Blocked once Delivered/Completed.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null) return NotFound();

        if (!CancellableStatuses.Contains(order.Status))
        {
            this.ToastError($"Order {order.OrderNumber} is {order.Status} and can no longer be cancelled.");
            return RedirectToAction(nameof(Index));
        }

        try
        {
            order.Status = OrderStatus.Cancelled;

            // One round trip for every item on the order, instead of one per line.
            await _inventoryService.IncrementStockBatchAsync(
                order.OrderItems.Select(i => (i.ProductId, i.Quantity)),
                InventoryChangeReason.OrderCancelled);

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = _userManager.GetUserId(User),
                Action = "OrderCancelled",
                Details = $"Cancelled order {order.OrderNumber}.{(string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}")}"
            });
            await _context.SaveChangesAsync();

            this.ToastSuccess($"Order {order.OrderNumber} was cancelled and stock restored.");

            if (order.Customer is not null && !string.IsNullOrWhiteSpace(order.Customer.Email))
            {
                await _emailSender.SendAsync(
                    order.Customer.Email,
                    $"Order {order.OrderNumber} cancelled",
                    $"<p>Hi {order.Customer.FullName},</p><p>Your order <strong>{order.OrderNumber}</strong> has been cancelled." +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}") + "</p>");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order {OrderId}.", id);
            this.ToastError("Something went wrong cancelling this order - please try again.");
        }

        return RedirectToAction(nameof(Index));
    }
}
