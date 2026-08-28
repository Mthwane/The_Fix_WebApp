
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Self-service area for Customer accounts: manage personal details (US-11),
/// view purchase history (US-12) and track order status (US-13).
/// </summary>
[Authorize(Roles = "Customer")] // customer self-service area, not permission-gated
public class CustomerController : Controller
{
    /// <summary>A customer can only cancel their own order while it's still Pending/Processing -
    /// once it's Shipped, staff need to handle it (see OrdersController).</summary>
    private static readonly OrderStatus[] CustomerCancellableStatuses = { OrderStatus.Pending, OrderStatus.Processing };

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<CustomerController> _logger;

    public CustomerController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IInventoryService inventoryService,
        ILogger<CustomerController> logger)
    {
        _context = context;
        _userManager = userManager;
        _inventoryService = inventoryService;
        _logger = logger;
    }

    // GET: /Customer/Orders?category=&search= - purchase history with live order status (US-12, US-13).
    [HttpGet]
    public async Task<IActionResult> Orders(OrderCategory? category, string? search)
    {
        var userId = _userManager.GetUserId(User);

        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == userId)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .AsQueryable();

        if (category.HasValue)
        {
            var statuses = OrderCategorizer.StatusesFor[category.Value];
            query = query.Where(o => statuses.Contains(o.Status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.OrderNumber.Contains(term));
        }

        // Tab counts computed from the customer's full order history (ignoring the search box,
        // so the badges always reflect "how many of mine are in this state" regardless of
        // whatever's currently typed in the search field).
        var allOrders = _context.Orders.AsNoTracking().Where(o => o.CustomerId == userId);
        ViewBag.CategoryCounts = new Dictionary<OrderCategory, int>
        {
            [OrderCategory.Pending] = await allOrders.CountAsync(o => OrderCategorizer.StatusesFor[OrderCategory.Pending].Contains(o.Status)),
            [OrderCategory.Completed] = await allOrders.CountAsync(o => OrderCategorizer.StatusesFor[OrderCategory.Completed].Contains(o.Status)),
            [OrderCategory.Past] = await allOrders.CountAsync(o => OrderCategorizer.StatusesFor[OrderCategory.Past].Contains(o.Status)),
        };
        ViewBag.AllCount = await allOrders.CountAsync();
        ViewBag.SelectedCategory = category;
        ViewBag.Search = search;

        var orders = await query.OrderByDescending(o => o.DateCreated).ToListAsync();

        ViewBag.CancellableStatuses = CustomerCancellableStatuses;
        return View(orders);
    }

    // POST: /Customer/CancelOrder/5 - self-service cancellation while it's still early enough.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var userId = _userManager.GetUserId(User);

        var order = await _context.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.OrderId == id && o.CustomerId == userId);

        if (order is null) return NotFound();

        if (!CustomerCancellableStatuses.Contains(order.Status))
        {
            this.ToastError($"Order {order.OrderNumber} is already {order.Status} and can no longer be cancelled here - please contact the store.");
            return RedirectToAction(nameof(Orders));
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
                UserId = userId,
                Action = "OrderCancelled",
                Details = $"Customer cancelled their own order {order.OrderNumber}."
            });
            await _context.SaveChangesAsync();

            this.ToastSuccess($"Order {order.OrderNumber} has been cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Customer {UserId} failed to cancel order {OrderId}.", userId, id);
            this.ToastError("Something went wrong cancelling your order - please try again.");
        }

        return RedirectToAction(nameof(Orders));
    }

    // GET: /Customer/Profile - view/update personal information (US-11).
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var model = new CustomerProfileViewModel
        {
            CustomerId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber
        };

        return View(model);
    }

    // POST: /Customer/Profile
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(CustomerProfileViewModel model)
    {
        var currentUserId = _userManager.GetUserId(User) ?? string.Empty;
        model.CustomerId = currentUserId; // never trust/bind this from the posted form

        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing is not null && existing.Id != user.Id)
            {
                ModelState.AddModelError(nameof(model.Email), "That email is already registered to another account.");
                return View(model);
            }

            await _userManager.SetEmailAsync(user, model.Email);
        }

        user.FullName = model.FullName;
        user.PhoneNumber = model.PhoneNumber;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Action = "ProfileUpdated",
            Details = $"'{user.UserName}' updated their personal information."
        });
        await _context.SaveChangesAsync();

        TempData["ProfileMessage"] = "Your details have been updated.";
        this.ToastSuccess("Your profile has been updated.");
        return RedirectToAction(nameof(Profile));
    }
}

