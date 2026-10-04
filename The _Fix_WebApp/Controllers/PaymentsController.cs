using System.Globalization;
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

namespace FashionFix.Web.Controllers;

/// <summary>
/// Handles the Paystack redirect back into the app. This is where an Order actually
/// gets created for a new-card payment - never in ShopController.Checkout - so nothing is marked paid,
/// and no stock is decremented, until the gateway has confirmed the money moved.
/// (The other place an Order can be created is ShopController.Checkout itself, for the
/// "charge a saved card instantly" and "pay from FixCash" paths, which never leave this app.)
///
/// Whenever the card has been charged but the order can't be completed - session expired, stock sold out
/// while paying, amount mismatch, a database fault - the case goes to IPaymentRecoveryService, which
/// refunds/restores automatically where it safely can and records a PaymentIncident for staff otherwise.
/// </summary>
[Authorize(Roles = "Customer")]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderFulfillmentService _orderFulfillment;
    private readonly IWalletService _wallet;
    private readonly IRewardsService _rewards;
    private readonly IDiscountService _discounts;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        ApplicationDbContext context,
        IOrderFulfillmentService orderFulfillment,
        IWalletService wallet,
        IRewardsService rewards,
        IDiscountService discounts,
        UserManager<ApplicationUser> userManager,
        ILogger<PaymentsController> logger)
    {
        _context = context;
        _orderFulfillment = orderFulfillment;
        _wallet = wallet;
        _rewards = rewards;
        _discounts = discounts;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Payments/Callback?reference=WEB-xxxx&trxref=WEB-xxxx
    // Paystack sends both "reference" and "trxref" with the same value - either is fine.
    [HttpGet]
    public async Task<IActionResult> Callback(
        string? reference,
        string? trxref,
        [FromServices] IPaymentService payments,
        [FromServices] IPaymentRecoveryService recovery)
    {
        var actualReference = reference ?? trxref;
        var pendingReference = HttpContext.Session.GetString("PendingPaymentReference");
        var pendingMethodRaw = HttpContext.Session.GetString("PendingPaymentMethod");
        var pendingAddressId = HttpContext.Session.GetInt32("PendingAddressId");
        var pendingSaveCard = HttpContext.Session.GetString("PendingSaveCard") == "true";

        if (string.IsNullOrEmpty(actualReference) || actualReference != pendingReference)
        {
            // A refresh/double-click after success lands here with the session already cleared - if the order
            // exists, just show it instead of an error.
            if (!string.IsNullOrEmpty(actualReference))
            {
                var userIdForLookup = _userManager.GetUserId(User);
                var done = await _context.Orders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrderNumber == actualReference && o.CustomerId == userIdForLookup);
                if (done is not null) return RedirectToAction("Confirmation", "Shop", new { id = done.OrderId });
            }

            this.ToastError("This payment session doesn't match your cart - please try checking out again.");
            return RedirectToAction("Cart", "Shop");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        // Double-click on the callback: the first request already created the order.
        var alreadyPlaced = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderNumber == actualReference);
        if (alreadyPlaced is not null)
            return RedirectToAction("Confirmation", "Shop", new { id = alreadyPlaced.OrderId });

        var verifyResult = await payments.VerifyTransactionAsync(actualReference);
        if (!verifyResult.Success)
        {
            this.ToastError($"Payment was not completed: {verifyResult.ErrorMessage}");
            return RedirectToAction("Cart", "Shop");
        }

        var cardCharged = verifyResult.AmountRands;

        // Hands everything back and tells the customer. Pending values are cleared so the same session
        // can't try to reuse this reference.
        async Task<IActionResult> FailAsync(string reason, decimal walletDebited, int pointsRedeemed, bool autoRefundCard = true)
        {
            var outcome = await recovery.HandleFailedOrderAsync(new PaymentFailureContext
            {
                Reference = actualReference,
                CustomerId = user.Id,
                CustomerEmail = user.Email,
                CustomerName = user.FullName,
                Source = PaymentIncidentSource.PaystackCheckout,
                CardCharged = cardCharged,
                WalletDebited = walletDebited,
                PointsRedeemed = pointsRedeemed,
                AutoRefundCard = autoRefundCard,
                Reason = reason
            });

            ClearPending(actualReference);
            this.ToastError(outcome.CustomerMessage);
            return RedirectToAction("Index", "Shop");
        }

        var cart = SessionCart.GetSnapshot(HttpContext.Session, actualReference);
        if (cart is null || cart.Lines.Count == 0)
        {
            // Verified payment but the session was lost. Nothing else was taken yet (wallet and points are only
            // taken further down), so this is a pure card refund.
            return await FailAsync("Payment verified but the cart session had expired.", 0m, 0);
        }

        // Reconcile what was actually charged against what this snapshot says the card should have paid.
        // The snapshot closes the "edit your cart while sitting on Paystack's page" window; this check is the
        // belt-and-braces backstop against any mismatch (rounding edge case, gateway anomaly).
        var pendingPoints = HttpContext.Session.GetInt32("PendingPointsRedeemed") ?? 0;
        var pendingPointsDiscount = 0m;
        if (pendingPoints > 0)
            decimal.TryParse(HttpContext.Session.GetString("PendingPointsDiscount"), NumberStyles.Number, CultureInfo.InvariantCulture, out pendingPointsDiscount);
        decimal.TryParse(HttpContext.Session.GetString("PendingWalletAmount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var pendingWallet);

        // The discount code was validated when checkout started. Re-validate it now against the snapshot basket: if it
        // can't be honoured any more (switched off / ran out while the customer was on Paystack) the card has already
        // been charged the discounted amount, so hand the money back rather than create a mispriced order.
        var pendingDiscountCode = HttpContext.Session.GetString("PendingDiscountCode");
        var pendingDiscountAmount = 0m;
        if (!string.IsNullOrWhiteSpace(pendingDiscountCode))
        {
            decimal.TryParse(HttpContext.Session.GetString("PendingDiscountAmount"), NumberStyles.Number, CultureInfo.InvariantCulture, out pendingDiscountAmount);

            var variantIdsInCart = cart.Lines.Select(l => l.VariantId).Distinct().ToList();
            var productByVariant = await _context.ProductVariants.AsNoTracking()
                .Where(v => variantIdsInCart.Contains(v.ProductVariantId))
                .ToDictionaryAsync(v => v.ProductVariantId, v => v.ProductId);
            var discountLines = cart.Lines
                .Where(l => productByVariant.ContainsKey(l.VariantId))
                .Select(l => new DiscountLine(productByVariant[l.VariantId], l.Quantity, l.UnitPrice))
                .ToList();

            // Nothing is redeemed until the order exists, so a plain re-evaluation is correct here.
            var recheck = await _discounts.EvaluateAsync(pendingDiscountCode, discountLines, DiscountChannel.Online, user.Id);
            if (!recheck.IsValid || Math.Abs(recheck.Amount - pendingDiscountAmount) > 0.01m)
                return await FailAsync($"Discount code {pendingDiscountCode} could no longer be honoured after payment ({recheck.Error ?? "amount changed"}).", 0m, 0);
        }
        else
        {
            pendingDiscountCode = null;
        }

        var expectedVat = TaxSettings.CalculateVat(cart.SubTotal, pendingPointsDiscount + pendingDiscountAmount);
        var expectedOrderTotal = cart.SubTotal - pendingPointsDiscount - pendingDiscountAmount + expectedVat
                                 + DeliverySettings.CalculateFee(cart.SubTotal); // same flat-fee rule the checkout used
        var expectedCardTotal = expectedOrderTotal - pendingWallet; // what Paystack should have charged: the CARD portion only

        if (Math.Abs(expectedCardTotal - cardCharged) > 0.01m)
        {
            _logger.LogError(
                "Payment {Reference} verified for {Paid:C} but the reconciled card total is {Expected:C} - not creating an order automatically.",
                actualReference, cardCharged, expectedCardTotal);

            // Suspicious: don't refund automatically - leave it for a person to look at.
            return await FailAsync($"Amount mismatch: paid {cardCharged:0.00}, expected {expectedCardTotal:0.00}.", 0m, 0, autoRefundCard: false);
        }

        // Final stock re-check - time has passed while the customer was on Paystack's page.
        var checkoutVariantIds = cart.Lines.Select(l => l.VariantId).Distinct().ToList();
        var checkoutVariants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => checkoutVariantIds.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        foreach (var line in cart.Lines)
        {
            if (!checkoutVariants.TryGetValue(line.VariantId, out var variant) || !variant.IsActive || !variant.Product.IsActive || variant.StockQuantity < line.Quantity)
                return await FailAsync($"'{line.Name}' sold out or was withdrawn while the customer was paying.", 0m, 0);
        }

        var paymentMethod = Enum.TryParse<PaymentMethod>(pendingMethodRaw, out var pm)
            ? pm
            : PaymentMethod.CreditCard;

        CustomerAddress? deliveryAddress = pendingAddressId.HasValue
            ? await _context.CustomerAddresses.FirstOrDefaultAsync(a => a.CustomerAddressId == pendingAddressId && a.CustomerId == user.Id)
            : null;

        // --- Take the reward points and the FixCash portion now that the card payment is confirmed ---
        var pointsRedeemed = 0;
        if (pendingPoints > 0)
        {
            var redeemResult = await _rewards.RedeemAsync(user.Id, pendingPoints, actualReference);
            if (!redeemResult.Success)
                return await FailAsync($"Could not redeem {pendingPoints} points: {redeemResult.ErrorMessage}", 0m, 0);
            pointsRedeemed = pendingPoints;
        }

        var walletDebited = 0m;
        if (pendingWallet > 0)
        {
            var debit = await _wallet.DebitForOrderAsync(user.Id, pendingWallet, actualReference);
            if (!debit.Success)
                return await FailAsync($"Could not debit FixCash {pendingWallet:0.00}: {debit.ErrorMessage}", 0m, pointsRedeemed);
            walletDebited = pendingWallet;
        }

        Order order;
        try
        {
            order = await _orderFulfillment.CreateOnlineOrderAsync(
                user, cart, paymentMethod, actualReference, deliveryAddress, pendingPointsDiscount, pendingWallet,
                pendingDiscountCode, pendingDiscountAmount);
        }
        catch (Exception ex)
        {
            return await FailAsync(
                ex is InsufficientStockException ? $"Sold out while paying: {ex.Message}" : $"Order creation failed: {ex.Message}",
                walletDebited, pointsRedeemed);
        }

        if (walletDebited > 0) await _wallet.LinkOrderAsync(actualReference, order.OrderId);

        // If the customer ticked "save this card" and the bank allows the card to be charged again later,
        // remember it for next time - dedup by AuthorizationCode so paying with the same card twice doesn't
        // create two entries.
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
        ClearPending(actualReference);

        this.ToastSuccess($"Payment confirmed - order {order.OrderNumber} placed for {order.GrandTotal:C}.");

        return RedirectToAction("Confirmation", "Shop", new { id = order.OrderId });
    }

    private void ClearPending(string reference)
    {
        SessionCart.ClearSnapshot(HttpContext.Session, reference);
        foreach (var key in new[]
                 {
                     "PendingPaymentReference", "PendingPaymentMethod", "PendingAddressId", "PendingSaveCard",
                     "PendingPointsRedeemed", "PendingPointsDiscount", "PendingWalletAmount",
                     "PendingDiscountCode", "PendingDiscountAmount"
                 })
            HttpContext.Session.Remove(key);
    }
}
