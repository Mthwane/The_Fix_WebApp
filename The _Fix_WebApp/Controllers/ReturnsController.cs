using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Over-the-counter returns. Ported from V1 with one substantive change: a resalable return now
/// restocks the exact ProductVariant that came back, not the parent style.
/// </summary>
[Authorize(Policy = Permissions.ReturnsProcess)]
public class ReturnsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IInventoryService _inventoryService;

    public ReturnsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IInventoryService inventoryService)
    {
        _context = context;
        _userManager = userManager;
        _inventoryService = inventoryService;
    }

    // GET: /Returns - recent returns, newest first.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var returns = await _context.ReturnTransactions
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.OrderItem).ThenInclude(i => i.Product)
            .Include(r => r.ProductVariant)
            .Include(r => r.ProcessedByUser)
            .OrderByDescending(r => r.DateProcessed)
            .Take(100)
            .ToListAsync();

        return View(returns);
    }

    // GET: /Returns/Lookup?orderNumber=WEB-123 - find the order to return against.
    [HttpGet]
    public async Task<IActionResult> Lookup(string? orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return View(null);

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OrderItems).ThenInclude(i => i.Product)
            .Include(o => o.OrderItems).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order is null)
        {
            this.ToastError($"No order found with number '{orderNumber}'.");
            return View(null);
        }

        // Show what's already been returned per line, so staff can't over-refund a line by
        // processing the same return twice.
        ViewBag.AlreadyReturned = await _context.ReturnTransactions
            .Where(r => r.OrderId == order.OrderId)
            .GroupBy(r => r.OrderItemId)
            .Select(g => new { OrderItemId = g.Key, Quantity = g.Sum(x => x.QuantityReturned) })
            .ToDictionaryAsync(x => x.OrderItemId, x => x.Quantity);

        ViewBag.OrderNumber = orderNumber;
        return View(order);
    }

    // POST: /Returns/Process
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(int orderItemId, int quantity, bool isResalable, RefundMethod refundMethod, string? reason)
    {
        var item = await _context.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product)
            .Include(i => i.ProductVariant)
            .FirstOrDefaultAsync(i => i.OrderItemId == orderItemId);

        if (item is null) return NotFound();

        if (quantity <= 0)
        {
            this.ToastError("Enter a quantity greater than zero.");
            return RedirectToAction(nameof(Lookup), new { orderNumber = item.Order.OrderNumber });
        }

        var alreadyReturned = await _context.ReturnTransactions
            .Where(r => r.OrderItemId == orderItemId)
            .SumAsync(r => (int?)r.QuantityReturned) ?? 0;

        var returnable = item.Quantity - alreadyReturned;
        if (quantity > returnable)
        {
            this.ToastError($"Only {returnable} unit(s) left to return on that line ({alreadyReturned} already returned).");
            return RedirectToAction(nameof(Lookup), new { orderNumber = item.Order.OrderNumber });
        }

        var refundAmount = item.UnitPrice * quantity;

        _context.ReturnTransactions.Add(new ReturnTransaction
        {
            OrderId = item.OrderId,
            OrderItemId = item.OrderItemId,
            ProductVariantId = item.ProductVariantId,
            ProcessedByUserId = _userManager.GetUserId(User)!,
            QuantityReturned = quantity,
            IsResalable = isResalable,
            RefundMethod = refundMethod,
            RefundAmount = refundAmount,
            Reason = reason
        });

        // Only resalable stock goes back on the shelf. Damaged goods are still refunded but
        // written off - incrementing stock for them would silently inflate inventory.
        if (isResalable && item.ProductVariantId.HasValue)
        {
            await _inventoryService.IncrementStockAsync(item.ProductVariantId.Value, quantity, InventoryChangeReason.Return);
        }
        else if (isResalable)
        {
            // Pre-variant historical line: refund stands, but there's no variant to restock into.
            this.ToastWarning("Refund processed, but this is a legacy order line with no size/colour recorded - restock it manually.");
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "ReturnProcessed",
            Details = $"Returned {quantity}x {item.Product?.Name} on {item.Order.OrderNumber}. Refund {refundAmount:C} via {refundMethod}. Resalable: {isResalable}."
        });

        await _context.SaveChangesAsync();

        this.ToastSuccess($"Return processed - {refundAmount:C} refunded via {refundMethod}.");
        return RedirectToAction(nameof(Lookup), new { orderNumber = item.Order.OrderNumber });
    }
}
