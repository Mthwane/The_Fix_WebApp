namespace FashionFix.Web.Security;

/// <summary>
/// The one place the online delivery-fee rule lives. Like TaxSettings, it is only ever evaluated
/// server-side - nothing numeric is trusted from the browser; the cart/checkout pages just display it.
///
/// Rule: an online basket worth LESS than R500 pays a flat R100 delivery fee; R500 or more is free.
/// The threshold is measured on the basket subtotal BEFORE discount codes / reward points, so using a
/// code never accidentally pushes a customer under the free-delivery line. In-store (POS) sales are
/// never charged delivery. The fee is a flat, VAT-inclusive amount (no extra VAT is added on top).
/// </summary>
public static class DeliverySettings
{
    public const decimal FreeDeliveryThreshold = 500m;
    public const decimal FlatFee = 100m;

    public static decimal CalculateFee(decimal subTotal) =>
        subTotal >= FreeDeliveryThreshold ? 0m : FlatFee;

    /// <summary>How much more the customer must add to reach free delivery (0 when already free).</summary>
    public static decimal AmountToFreeDelivery(decimal subTotal) =>
        Math.Max(0m, FreeDeliveryThreshold - subTotal);
}
