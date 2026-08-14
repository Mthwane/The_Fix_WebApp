using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>Process returns and exchanges against a valid receipt or transaction ID (US-09).</summary>
[Authorize(Roles = "Administrator,Manager,Employee")]
public class ReturnsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReturnsController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _inventoryService = inventoryService;
        _userManager = userManager;
    }

    // GET: /Returns
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var returns = await _context.ReturnTransactions
            .Include(r => r.Order)
            .Include(r => r.OrderItem).ThenInclude(oi => oi.Product)
            .OrderByDescending(r => r.DateProcessed)
            .Take(50)
            .ToListAsync();

        return View(returns);
    }

    // GET: /Returns/Lookup?orderNumber= - find a sale by receipt/transaction ID.
    [HttpGet]
    public async Task<IActionResult> Lookup(string orderNumber)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        return order is null ? NotFound() : View(order);
    }

    // POST: /Returns/Process - reverses the sale, restocks if resalable, issues refund/credit.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(int orderItemId, int quantity, bool isResalable, RefundMethod refundMethod)
    {
        var orderItem = await _context.OrderItems
            .Include(oi => oi.Order)
            .Include(oi => oi.Product)
            .FirstOrDefaultAsync(oi => oi.OrderItemId == orderItemId);

        if (orderItem is null) return NotFound();

        var refundAmount = orderItem.UnitPrice * quantity;
        var processedByUserId = _userManager.GetUserId(User) ?? string.Empty;

        _context.ReturnTransactions.Add(new ReturnTransaction
        {
            OrderId = orderItem.OrderId,
            OrderItemId = orderItem.OrderItemId,
            ProcessedByUserId = processedByUserId,
            QuantityReturned = quantity,
            IsResalable = isResalable,
            RefundMethod = refundMethod,
            RefundAmount = refundAmount
        });

        orderItem.Order.Status = OrderStatus.Returned;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = processedByUserId,
            Action = "ReturnProcessed",
            // NOTE: store-credit ledger integration (when RefundMethod == StoreCredit) is a
            // follow-up item - for now the credit is recorded here but not yet redeemable.
            Details = $"Returned {quantity}x '{orderItem.Product.Name}' from order {orderItem.Order.OrderNumber} - {refundAmount:C} via {refundMethod}."
        });

        await _context.SaveChangesAsync();

        if (isResalable)
            await _inventoryService.IncrementStockAsync(orderItem.ProductId, quantity, InventoryChangeReason.Return);

        return RedirectToAction(nameof(Index));
    }
}
