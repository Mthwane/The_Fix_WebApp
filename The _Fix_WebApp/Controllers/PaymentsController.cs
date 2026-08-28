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
/// (The other place an Order can be created is ShopController.Checkout itself, but only
/// for the "charge a saved card instantly" path, which never leaves this app at all.)
/// </summary>
[Authorize(Roles = "Customer")]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderFulfillmentService _orderFulfillment;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        ApplicationDbContext context,
        IOrderFulfillmentService orderFulfillment,
        UserManager<ApplicationUser> userManager,
        ILogger<PaymentsController> logger)
    {
        _context = context;
        _orderFulfillment = orderFulfillment;
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
        var pendingAddressId = HttpContext.Session.GetInt32("PendingAddressId");
        var pendingSaveCard = HttpContext.Session.GetString("PendingSaveCard") == "true";

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

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var paymentMethod = Enum.TryParse<PaymentMethod>(pendingMethodRaw, out var pm)
            ? pm
            : PaymentMethod.CreditCard;

        CustomerAddress? deliveryAddress = pendingAddressId.HasValue
            ? await _context.CustomerAddresses.FirstOrDefaultAsync(a => a.CustomerAddressId == pendingAddressId && a.CustomerId == user.Id)
            : null;

        var order = await _orderFulfillment.CreateOnlineOrderAsync(user, cart, paymentMethod, actualReference, deliveryAddress);

        // If the customer ticked "save this card" and the bank allows the card to be
        // charged again later, remember it for next time - dedup by AuthorizationCode so
        // paying with the same card twice doesn't create two entries.
        if (pendingSaveCard && verifyResult.Authorization is { Reusable: true } auth)
        {
            var alreadySaved = await _context.CustomerPaymentMethods
                .AnyAsync(p => p.CustomerId == user.Id && p.AuthorizationCode == auth.AuthorizationCode);

            if (!alreadySaved)
            {
                var hasAnyCard = await _context.CustomerPaymentMethods.AnyAsync(p => p.CustomerId == user.Id);
                _context.CustomerPaymentMethods.Add(new CustomerPaymentMethod
                {
                    CustomerId = user.Id,
                    AuthorizationCode = auth.AuthorizationCode,
                    Last4 = auth.Last4,
                    CardType = auth.CardType,
                    ExpiryMonth = auth.ExpiryMonth,
                    ExpiryYear = auth.ExpiryYear,
                    Bank = auth.Bank,
                    IsDefault = !hasAnyCard // first saved card becomes the default automatically
                });
                await _context.SaveChangesAsync();
            }
        }

        SessionCart.Clear(HttpContext.Session);
        HttpContext.Session.Remove("PendingPaymentReference");
        HttpContext.Session.Remove("PendingPaymentMethod");
        HttpContext.Session.Remove("PendingAddressId");
        HttpContext.Session.Remove("PendingSaveCard");

        this.ToastSuccess($"Payment confirmed - order {order.OrderNumber} placed for {order.GrandTotal:C}.");

        return RedirectToAction("Confirmation", "Shop", new { id = order.OrderId });
    }
}