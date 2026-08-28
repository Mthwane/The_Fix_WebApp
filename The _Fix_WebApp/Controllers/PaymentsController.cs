using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using The__Fix_WebApp.Services;
using System.Net;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Handles the Paystack redirect back into the app. This is where an Order actually
/// gets created - never in ShopController.Checkout - so nothing is marked paid,
/// and no stock is decremented, until the gateway has confirmed the money moved.
/// </summary>
[Authorize(Roles = "Customer")]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        ILogger<PaymentsController> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Payments/Callback?reference=WEB-xxxx&trxref=WEB-xxxx
    // Paystack sends both "reference" and "trxref" with the same value - either is fine.
    [HttpGet]
    public async Task<IActionResult> Callback(
        string? reference, string? trxref, [FromServices] IPaymentService payments)
    {
        var actualReference = reference ?? trxref;
        var pendingReference = HttpContext.Session.GetString("PendingPaymentReference");
        var pendingMethodRaw = HttpContext.Session.GetString("PendingPaymentMethod");

        if (string.IsNullOrEmpty(actualReference) || actualReference != pendingReference)
        {
            this.ToastError("This payment session doesn't match your cart - please try checking out again.");
            return RedirectToAction("Cart", "Shop");
        }

        var verifyResult = await payments.VerifyTransactionAsync(actualReference);

        if (!verifyResult.Success)
        {
            this.ToastError($"Payment was not completed: {verifyResult.ErrorMessage}");
            return RedirectToAction("Cart", "Shop");
        }

        var cart = SessionCart.Get(HttpContext.Session);
        if (cart.Lines.Count == 0)
        {
            // Verified payment but no cart left in session - shouldn't normally happen,
            // but don't silently lose a paid transaction: log it loudly for manual follow-up.
            _logger.LogError(
                "Payment {Reference} verified for {Amount:C} but no cart was found in session.",
                actualReference, verifyResult.AmountRands);
            this.ToastError("Your payment succeeded but your cart session expired. Please contact support with reference " + actualReference);
            return RedirectToAction("Index", "Shop");
        }

        // Final stock re-check - time has passed while the customer was on Paystack's page.
        // One IN-clause query for the whole cart instead of one FindAsync per line.
        var checkoutProductIds = cart.Lines.Select(l => l.ProductId).Distinct().ToList();
        var checkoutProducts = await _context.Products
            .AsNoTracking()
            .Where(p => checkoutProductIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId);

        foreach (var line in cart.Lines)
        {
            if (!checkoutProducts.TryGetValue(line.ProductId, out var product) || !product.IsActive || product.StockQuantity < line.Quantity)
            {
                _logger.LogError(
                    "Payment {Reference} verified for {Amount:C} but stock check failed for product {ProductId}.",
                    actualReference, verifyResult.AmountRands, line.ProductId);
                this.ToastError($"Your payment succeeded but '{line.Name}' is no longer available. Please contact support with reference {actualReference} for a refund.");
                return RedirectToAction("Index", "Shop");
            }
        }

        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);

        var paymentMethod = Enum.TryParse<PaymentMethod>(pendingMethodRaw, out var pm)
            ? pm
            : PaymentMethod.CreditCard;

        var vat = TaxSettings.CalculateVat(cart.SubTotal);

        var order = new Order
        {
            OrderNumber = actualReference,
            OrderType = OrderType.Online,
            Status = OrderStatus.Processing,
            PaymentMethod = paymentMethod,
            CustomerId = userId,
            SubTotal = cart.SubTotal,
            DiscountTotal = 0,
            TaxTotal = vat,
            GrandTotal = cart.SubTotal + vat
        };

        foreach (var line in cart.Lines)
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

        // One round trip and one commit for the whole cart, instead of one query + one
        // commit per line item.
        var updatedProducts = await _inventoryService.DecrementStockBatchAsync(
            cart.Lines.Select(l => (l.ProductId, l.Quantity)));

        var lowStockProductIds = updatedProducts.Where(p => p.IsLowStock).Select(p => p.ProductId).ToHashSet();
        var newlyLowStock = cart.Lines
            .Where(l => lowStockProductIds.Contains(l.ProductId))
            .Select(l => l.Name)
            .ToList();

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "OnlineOrderPlaced",
            Details = $"Placed order {order.OrderNumber} for {order.GrandTotal:C} ({cart.Lines.Count} line item(s)) via Paystack."
        });
        await _context.SaveChangesAsync();

        SessionCart.Clear(HttpContext.Session);
        HttpContext.Session.Remove("PendingPaymentReference");
        HttpContext.Session.Remove("PendingPaymentMethod");

        if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            var itemsHtml = string.Join("", cart.Lines.Select(l =>
                $"<tr><td>{l.Name}</td><td>{l.Quantity}</td><td>{l.UnitPrice:C}</td><td>{l.LineTotal:C}</td></tr>"));

            var body = $@"
                <h2>Thanks for your order, {user.FullName}!</h2>
                <p>Order <strong>{order.OrderNumber}</strong> has been received and is being processed.</p>
                <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                    <thead><tr><th>Item</th><th>Qty</th><th>Unit Price</th><th>Line Total</th></tr></thead>
                    <tbody>{itemsHtml}</tbody>
                </table>
                <p>Subtotal: {order.SubTotal:C}<br/>VAT (15%): {order.TaxTotal:C}<br/>
                <strong>Total: {order.GrandTotal:C}</strong></p>
                <p>You can track this order any time under My Orders.</p>";

            await _emailSender.SendAsync(user.Email, $"Order Confirmation - {order.OrderNumber}", body);
        }

        this.ToastSuccess($"Payment confirmed - order {order.OrderNumber} placed for {order.GrandTotal:C}.");

        if (newlyLowStock.Count > 0)
            _logger.LogInformation("Online order pushed these products into low stock: {Products}", string.Join(", ", newlyLowStock));

        return RedirectToAction("Confirmation", "Shop", new { id = order.OrderId });
    }
}