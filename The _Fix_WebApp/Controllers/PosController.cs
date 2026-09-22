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
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User);
        var today = DateTime.UtcNow.Date;

        var todaysSales = await _context.Orders
            .Where(o => o.ProcessedByUserId == userId && o.OrderType == OrderType.POS && o.DateCreated >= today)
            .ToListAsync();

        ViewBag.TodaysTillSales = todaysSales.Sum(o => o.GrandTotal);
        ViewBag.TodaysTillTransactionCount = todaysSales.Count;

        ViewBag.CurrentShift = await _context.ShiftSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        return View(new POSCheckoutViewModel());
    }

    // GET: /Pos/StartShift - opening float entry. Not a hard gate on using the till (POS
    // access isn't blocked without an open shift, to avoid a risky behavioural change to an
    // already-working screen) - this is opt-in, for stores that want the cash reconciliation.
    [HttpGet]
    public async Task<IActionResult> StartShift()
    {
        var userId = _userManager.GetUserId(User);
        var existing = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (existing is not null)
        {
            this.ToastWarning("You already have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        return View();
    }

    // POST: /Pos/StartShift
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartShift(decimal openingFloat)
    {
        var userId = _userManager.GetUserId(User)!;
        var existing = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (existing is not null)
        {
            this.ToastWarning("You already have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        if (openingFloat < 0)
        {
            this.ToastError("Opening float can't be negative.");
            return View();
        }

        _context.ShiftSessions.Add(new ShiftSession { UserId = userId, OpeningFloat = openingFloat });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Shift started with an opening float of {openingFloat:C}.");
        return RedirectToAction(nameof(Index));
    }

    // GET: /Pos/EndShift - shows expected cash (opening float + cash sales since open) so the
    // operator can count the drawer against it before confirming.
    [HttpGet]
    public async Task<IActionResult> EndShift()
    {
        var userId = _userManager.GetUserId(User);
        var shift = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (shift is null)
        {
            this.ToastWarning("You don't have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        var cashSales = await _context.Orders
            .Where(o => o.ProcessedByUserId == userId && o.PaymentMethod == PaymentMethod.Cash && o.DateCreated >= shift.DateOpened)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;

        ViewBag.ExpectedCash = shift.OpeningFloat + cashSales;
        return View(shift);
    }

    // POST: /Pos/EndShift
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndShift(decimal closingFloat, string? notes)
    {
        var userId = _userManager.GetUserId(User);
        var shift = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (shift is null)
        {
            this.ToastWarning("You don't have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        if (closingFloat < 0)
        {
            this.ToastError("Closing float can't be negative.");
            return RedirectToAction(nameof(EndShift));
        }

        var cashSales = await _context.Orders
            .Where(o => o.ProcessedByUserId == userId && o.PaymentMethod == PaymentMethod.Cash && o.DateCreated >= shift.DateOpened)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;
        var expectedCash = shift.OpeningFloat + cashSales;
        var variance = closingFloat - expectedCash;

        shift.ClosingFloat = closingFloat;
        shift.DateClosed = DateTime.UtcNow;
        shift.Status = ShiftStatus.Closed;
        shift.Notes = notes;
        await _context.SaveChangesAsync();

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "ShiftClosed",
            Details = $"Closed shift - expected {expectedCash:C}, counted {closingFloat:C}, variance {variance:C}."
        });
        await _context.SaveChangesAsync();

        if (Math.Abs(variance) < 0.01m)
            this.ToastSuccess($"Shift closed - drawer balanced exactly ({closingFloat:C}).");
        else
            this.ToastWarning($"Shift closed - {(variance > 0 ? "over" : "short")} by {Math.Abs(variance):C} (expected {expectedCash:C}, counted {closingFloat:C}).");

        return RedirectToAction(nameof(Index));
    }

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
        // etc.) - re-check right before committing so we never oversell. One query for the
        // whole basket (not one per line) via an IN-clause lookup, keyed to the exact
        // variant (size/colour) scanned, not just the parent product.
        var cartVariantIds = model.CartItems.Select(l => l.VariantId).Distinct().ToList();
        var currentVariants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => cartVariantIds.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        foreach (var line in model.CartItems)
        {
            if (!currentVariants.TryGetValue(line.VariantId, out var variant) || !variant.IsActive || !variant.Product.IsActive)
            {
                this.ToastError($"'{line.ProductName}' is no longer available - it's been removed from the till.");
                return View(nameof(Index), model);
            }
            if (variant.StockQuantity < line.Quantity)
            {
                this.ToastError($"Only {variant.StockQuantity} of '{line.ProductName}' ({variant.Size}/{variant.Color}) left in stock - please adjust the quantity.");
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
                    ProductVariantId = line.VariantId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineTotal = line.LineTotal
                });
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();


            // One round trip and one commit for the whole basket, instead of looping
            // DecrementStockAsync + IsLowStockAsync per line (which was N queries + N
            // separate commits for an N-item sale).
            var updatedVariants = await _inventoryService.DecrementStockBatchAsync(
                model.CartItems.Select(l => (l.VariantId, l.Quantity)));

            var lowStockVariantIds = updatedVariants.Where(v => v.IsLowStock).Select(v => v.ProductVariantId).ToHashSet();
            var newlyLowStock = model.CartItems
                .Where(l => lowStockVariantIds.Contains(l.VariantId))
                .Select(l => $"{l.ProductName} ({l.Size}/{l.Color})")
                .ToList();


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
            .Include(o => o.OrderItems).ThenInclude(oi => oi.ProductVariant)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null) return NotFound();
        return View(order);
    }

    // GET: /Pos/Product/{sku} - AJAX lookup used by the barcode-scanning JS in wwwroot/js.
    // "sku" here is now a VARIANT sku (e.g. "CLO-0001-M-BLK") - each size/colour is its own
    // scannable code, rather than one SKU per style.
    [HttpGet]
    public async Task<IActionResult> Product(string sku)
    {
        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.SKU == sku && v.IsActive && v.Product.IsActive)
            .Select(v => new
            {
                ProductId = v.ProductId,
                VariantId = v.ProductVariantId,
                v.Product.Name,
                Sku = v.SKU,
                SellingPrice = v.PriceOverride ?? v.Product.SellingPrice,
                v.StockQuantity,
                v.Product.Category,
                v.Size,
                v.Color,
                v.Product.Brand,
                v.Product.ImageUrl,
                v.Product.Description
            })
            .FirstOrDefaultAsync();

        return variant is null ? NotFound() : Json(variant);
    }
}
