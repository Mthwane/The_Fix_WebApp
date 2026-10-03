using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services;

/// <summary>One basket line as the discount engine sees it. Prices must come from the database/session, never the browser.</summary>
public record DiscountLine(int ProductId, int Quantity, decimal UnitPrice);

/// <summary>The outcome of checking a code against a basket.</summary>
public class DiscountResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public Discount? Discount { get; init; }

    /// <summary>Rand amount taken off the basket (already rounded and capped). 0 when invalid.</summary>
    public decimal Amount { get; init; }

    /// <summary>The part of the basket the code actually applies to (the whole basket, or just the linked products).</summary>
    public decimal EligibleSubtotal { get; init; }

    public static DiscountResult Fail(string error) => new() { IsValid = false, Error = error };
}

/// <summary>
/// Every discount rule lives here, so the till (PosController), online checkout (ShopController /
/// PaymentsController) and the order builder all agree. Nothing a browser sends is trusted: callers pass basket
/// lines priced from the database or the server-side session cart, and redemption is an atomic conditional
/// UPDATE, so two people can't both use the last redemption of a limited code.
/// </summary>
public interface IDiscountService
{
    /// <summary>Checks the code against the basket and works out the saving. Does NOT consume a redemption.</summary>
    Task<DiscountResult> EvaluateAsync(
        string? code,
        IReadOnlyCollection<DiscountLine> lines,
        DiscountChannel channel,
        string? customerId);

    /// <summary>
    /// Consumes one redemption and records it against the order. Must be called inside the same database
    /// transaction that creates the order (it joins the caller's transaction). Throws
    /// <see cref="DiscountUnavailableException"/> if the code ran out or was switched off in the meantime.
    /// The caller's next SaveChanges persists the redemption row.
    /// </summary>
    Task RedeemAsync(string code, int orderId, string? customerId, decimal amount);

    /// <summary>Live, redeemable discounts for a channel - what a cashier with the view permission sees at the till.</summary>
    Task<List<Discount>> GetLiveForChannelAsync(DiscountChannel channel);

    /// <summary>The newest live discount that has its storefront banner switched on, or null.</summary>
    Task<Discount?> GetActiveBannerAsync();

    /// <summary>Live, banner-enabled discounts linked to this product (directly, or via its category, brand or supplier).</summary>
    Task<List<Discount>> GetLinkedBannerDiscountsAsync(int productId, string category, string? brand, int? supplierId);

    /// <summary>For the staff catalogue: which live, product-linked discount codes cover each product (by id).
    /// Whole-basket discounts (no links) are left out.</summary>
    Task<Dictionary<int, List<string>>> GetLiveLinkedCodesByProductAsync(IReadOnlyCollection<Product> products);

    /// <summary>A random, unused, easy-to-read code such as FIX-7K3P9Q.</summary>
    Task<string> GenerateUniqueCodeAsync(string? prefix = null);

    /// <summary>Trims and upper-cases a typed code so it matches how codes are stored.</summary>
    string NormalizeCode(string? code);
}
