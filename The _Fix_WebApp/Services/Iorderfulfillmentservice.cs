using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;

namespace FashionFix.Web.Services;

/// <summary>
/// Turns a verified/paid cart into a real Order: creates the Order + OrderItems, decrements stock,
/// writes the audit log, awards reward points, and emails a confirmation. Shared by both places a
/// payment can be confirmed - PaymentsController.Callback (after a Paystack redirect) and
/// ShopController.Checkout (an instant charge against a saved card or FixCash, no redirect) - so the
/// paths can never drift out of sync.
///
/// The order row and its stock movement are committed in ONE database transaction, so a failure part
/// way through leaves nothing behind. Transient database faults are retried, and the call is
/// idempotent per reference: if an earlier attempt actually committed, the existing order is returned.
/// A stock shortage throws InsufficientStockException (retrying can't fix it); callers should hand any
/// failure to IPaymentRecoveryService so the customer's money is returned.
/// </summary>
public interface IOrderFulfillmentService
{
    /// <param name="pointsDiscount">Rand value of reward points already redeemed for this order (0 if none).</param>
    /// <param name="discountCode">A discount code already validated for this basket (null if none). It is redeemed
    /// atomically inside the order transaction; if it ran out meanwhile a DiscountUnavailableException is thrown.</param>
    /// <param name="discountAmount">The rand saving that code was validated for.</param>
    /// <param name="walletAmount">Rand amount already debited from the customer's FixCash wallet for this order
    /// (0 if none; equals the grand total for a full FixCash payment).</param>
    Task<Order> CreateOnlineOrderAsync(
        ApplicationUser customer,
        CartViewModel cart,
        PaymentMethod paymentMethod,
        string reference,
        CustomerAddress? deliveryAddress,
        decimal pointsDiscount = 0m,
        decimal walletAmount = 0m,
        string? discountCode = null,
        decimal discountAmount = 0m);
}
