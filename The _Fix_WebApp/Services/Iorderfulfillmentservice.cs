using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;

namespace FashionFix.Web.Services;

/// <summary>
/// Turns a verified/paid cart into a real Order: creates the Order + OrderItems, decrements
/// stock, writes the audit log, and emails a confirmation. Shared by both places a payment
/// can be confirmed - PaymentsController.Callback (after a Paystack redirect) and
/// ShopController.Checkout (an instant charge against a saved card, no redirect) - so the two
/// paths can never drift out of sync with each other.
/// </summary>
public interface IOrderFulfillmentService
{
    Task<Order> CreateOnlineOrderAsync(
        ApplicationUser customer,
        CartViewModel cart,
        PaymentMethod paymentMethod,
        string reference,
        CustomerAddress? deliveryAddress);
}