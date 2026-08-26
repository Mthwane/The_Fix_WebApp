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

    // GET: /Customer/Orders - purchase history with live order status (US-12, US-13).
    [HttpGet]
    public async Task<IActionResult> Orders()
    {
        var userId = _userManager.GetUserId(User);

        var orders = await _context.Orders
            .Where(o => o.CustomerId == userId)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .OrderByDescending(o => o.DateCreated)
            .ToListAsync();

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

            foreach (var item in order.OrderItems)
                await _inventoryService.IncrementStockAsync(item.ProductId, item.Quantity, InventoryChangeReason.OrderCancelled);

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
