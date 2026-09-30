using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;

namespace FashionFix.Web.Services;

/// <summary>
/// Turns a verified/paid cart into a real Order: creates the Order + OrderItems, decrements
/// stock, writes the audit log, awards reward points, and emails a confirmation. Shared by both places a payment
/// can be confirmed - PaymentsController.Callback (after a Paystack redirect) and
/// ShopController.Checkout (an instant charge against a saved card or FixCash, no redirect) - so the paths can never drift out of sync.
/// </summary>
public interface IOrderFulfillmentService
{
    /// <param name="pointsDiscount">Rand value of reward points already redeemed for this order (0 if none).
    /// The caller must have deducted the points first, using the same reference.</param>
    Task<Order> CreateOnlineOrderAsync(
        ApplicationUser customer,
        CartViewModel cart,
        PaymentMethod paymentMethod,
        string reference,
        CustomerAddress? deliveryAddress,
        decimal pointsDiscount = 0m);
}