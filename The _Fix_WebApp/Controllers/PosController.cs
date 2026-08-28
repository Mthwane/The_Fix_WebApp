using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace FashionFix.Web.Controllers;

[Authorize(Policy = Permissions.PosUse)]
public class PosController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PosController> _logger;

    public PosController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        ILogger<PosController> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Pos - the till interface for staff.
    [HttpGet]
    public IActionResult Index() => View(new POSCheckoutViewModel());

    // POST: /Pos/Checkout - scans/cart lines already built client-side (barcode JS), submitted here.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(POSCheckoutViewModel model)
    {
        // VAT is always recomputed here from the fixed rate - never trusted from the client,
        // and never a reason validation can fail (it used to be a free-typed field, which was
        // the #1 cause of checkout silently failing with no explanation to the cashier).
        model.TaxTotal = TaxSettings.CalculateVat(model.SubTotal, model.DiscountTotal);

        if (model.CartItems.Count == 0)
        {
            this.ToastError("The till is empty - scan at least one item before completing the sale.");
            return View(nameof(Index), model);
        }

        if (!ModelState.IsValid)
        {
            var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            this.ToastError(string.IsNullOrWhiteSpace(errors)
                ? "Couldn't complete the sale - please check the details and try again."
                : $"Couldn't complete the sale: {errors}");
            return View(nameof(Index), model);
        }

        // Stock can move between scanning and completing the sale (another till, a return,
<<<<<<< HEAD
        // etc.) - re-check right before committing so we never oversell. One query for the
        // whole basket (not one per line) via an IN-clause lookup.
        var cartProductIds = model.CartItems.Select(l => l.ProductId).Distinct().ToList();
        var currentProducts = await _context.Products
            .AsNoTracking()
            .Where(p => cartProductIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId);

        foreach (var line in model.CartItems)
        {
            if (!currentProducts.TryGetValue(line.ProductId, out var product) || !product.IsActive)
=======
        // etc.) - re-check right before committing so we never oversell.
        foreach (var line in model.CartItems)
        {
            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductId == line.ProductId);
            if (product is null || !product.IsActive)
>>>>>>> origin/SprintPresent
            {
                this.ToastError($"'{line.ProductName}' is no longer available - it's been removed from the till.");
                return View(nameof(Index), model);
            }
            if (product.StockQuantity < line.Quantity)
            {
                this.ToastError($"Only {product.StockQuantity} of '{line.ProductName}' left in stock - please adjust the quantity.");
                return View(nameof(Index), model);
            }
        }

        var cashierId = _userManager.GetUserId(User);

        try
        {
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

<<<<<<< HEAD
            // One round trip and one commit for the whole basket, instead of looping
            // DecrementStockAsync + IsLowStockAsync per line (which was N queries + N
            // separate commits for an N-item sale).
            var updatedProducts = await _inventoryService.DecrementStockBatchAsync(
                model.CartItems.Select(l => (l.ProductId, l.Quantity)));

            var lowStockProductIds = updatedProducts.Where(p => p.IsLowStock).Select(p => p.ProductId).ToHashSet();
            var newlyLowStock = model.CartItems
                .Where(l => lowStockProductIds.Contains(l.ProductId))
                .Select(l => l.ProductName)
                .ToList();
=======
            var newlyLowStock = new List<string>();
            foreach (var line in model.CartItems)
            {
                await _inventoryService.DecrementStockAsync(line.ProductId, line.Quantity);

                if (await _inventoryService.IsLowStockAsync(line.ProductId))
                    newlyLowStock.Add(line.ProductName);
            }
>>>>>>> origin/SprintPresent

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = cashierId,
                Action = "SaleProcessed",
                Details = $"Processed sale {order.OrderNumber} for {order.GrandTotal:C} ({model.CartItems.Count} line item(s))."
            });
            await _context.SaveChangesAsync();

            // Digital receipt (US-07): an explicit ReceiptEmail typed at the till takes
            // priority (covers walk-in customers with no account); otherwise fall back to
            // the linked customer account's email, if any.
            var recipientEmail = model.ReceiptEmail;
            var recipientName = "there";

            if (string.IsNullOrWhiteSpace(recipientEmail) && !string.IsNullOrWhiteSpace(model.CustomerId))
            {
                var linkedCustomer = await _userManager.FindByIdAsync(model.CustomerId);
                if (linkedCustomer is not null && !string.IsNullOrWhiteSpace(linkedCustomer.Email))
                {
                    recipientEmail = linkedCustomer.Email;
                    recipientName = linkedCustomer.FullName;
                }
            }

            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                var itemsHtml = string.Join("", model.CartItems.Select(l =>
                    $"<tr><td>{l.ProductName}</td><td>{l.Quantity}</td><td>{l.UnitPrice:C}</td><td>{l.LineTotal:C}</td></tr>"));

                var body = $@"
                    <h2>Thanks for shopping with us, {recipientName}!</h2>
                    <p>Receipt for order <strong>{order.OrderNumber}</strong> ({order.DateCreated:dd MMM yyyy, HH:mm}).</p>
                    <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                        <thead><tr><th>Item</th><th>Qty</th><th>Unit Price</th><th>Line Total</th></tr></thead>
                        <tbody>{itemsHtml}</tbody>
                    </table>
                    <p>Subtotal: {order.SubTotal:C}<br/>VAT (15%): {order.TaxTotal:C}<br/>
                    <strong>Total: {order.GrandTotal:C}</strong> (paid via {order.PaymentMethod})</p>";

                await _emailSender.SendAsync(recipientEmail, $"Receipt - {order.OrderNumber}", body);
            }

            this.ToastSuccess($"Sale {order.OrderNumber} completed - {order.GrandTotal:C} ({model.CartItems.Count} item(s))." +
                (string.IsNullOrWhiteSpace(recipientEmail) ? "" : $" Receipt emailed to {recipientEmail}."));

            if (newlyLowStock.Count > 0)
                this.ToastWarning($"Now low on stock: {string.Join(", ", newlyLowStock)}.");

            return RedirectToAction(nameof(Receipt), new { id = order.OrderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POS checkout failed for cashier {CashierId} with {ItemCount} item(s).", cashierId, model.CartItems.Count);
            this.ToastError("Something went wrong completing the sale. Nothing was charged - please try again.");
            return View(nameof(Index), model);
        }
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
<<<<<<< HEAD
            .AsNoTracking()
=======
>>>>>>> origin/SprintPresent
            .Where(p => p.SKU == sku && p.IsActive)
            .Select(p => new
            {
                p.ProductId,
                p.Name,
                p.SKU,
                p.SellingPrice,
                p.StockQuantity,
                p.Category,
                p.Size,
                p.Color,
                p.Brand,
                p.ImageUrl,
                p.Description
            })
            .FirstOrDefaultAsync();

        return product is null ? NotFound() : Json(product);
    }
}
