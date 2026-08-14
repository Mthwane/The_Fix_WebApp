using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

[Authorize(Roles = "Administrator,Manager,Employee")]
public class PosController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PosController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _inventoryService = inventoryService;
        _userManager = userManager;
    }

    // GET: /Pos - the till interface for staff.
    [HttpGet]
    public IActionResult Index() => View(new POSCheckoutViewModel());

    // POST: /Pos/Checkout - scans/cart lines already built client-side (barcode JS), submitted here.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(POSCheckoutViewModel model)
    {
        if (!ModelState.IsValid || model.CartItems.Count == 0)
            return View(nameof(Index), model);

        var cashierId = _userManager.GetUserId(User);

        var order = new Order
        {
            OrderNumber = $"POS-{DateTime.UtcNow:yyyyMMddHHmmss}",
            OrderType = OrderType.POS,
            Status = OrderStatus.Completed,
            PaymentMethod = model.PaymentMethod,
            CustomerId = model.CustomerId,
            ProcessedByUserId = cashierId,
            SubTotal = model.SubTotal,
            DiscountTotal = model.DiscountTotal,
            TaxTotal = model.TaxTotal,
            GrandTotal = model.GrandTotal,
            DateFulfilled = DateTime.UtcNow
        };

        foreach (var line in model.CartItems)
        {
            order.OrderItems.Add(new OrderItem
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal
            });
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        foreach (var line in model.CartItems)
        {
            await _inventoryService.DecrementStockAsync(line.ProductId, line.Quantity);
        }

        // TODO: write an AuditLog entry ("SaleProcessed") and trigger Receipt generation.
        return RedirectToAction(nameof(Receipt), new { id = order.OrderId });
    }

    // GET: /Pos/Receipt/5
    [HttpGet]
    public async Task<IActionResult> Receipt(int id)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null) return NotFound();
        return View(order);
    }

    // GET: /Pos/Product/{sku} - AJAX lookup used by the barcode-scanning JS in wwwroot/js.
    [HttpGet]
    public async Task<IActionResult> Product(string sku)
    {
        var product = await _context.Products
            .Where(p => p.SKU == sku && p.IsActive)
            .Select(p => new { p.ProductId, p.Name, p.SKU, p.SellingPrice, p.StockQuantity })
            .FirstOrDefaultAsync();

        return product is null ? NotFound() : Json(product);
    }
}
