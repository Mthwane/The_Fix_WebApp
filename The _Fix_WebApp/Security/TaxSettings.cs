namespace FashionFix.Web.Security;

/// <summary>
/// Tax is computed server-side everywhere a sale happens (POS, online checkout) - never
/// trusted from client input. This is the one place the rate is defined.
/// </summary>
public static class TaxSettings
{
    /// <summary>South African VAT rate.</summary>
    public const decimal VatRate = 0.15m;

    public static decimal CalculateVat(decimal subTotal, decimal discount = 0)
    {
        var taxableAmount = subTotal - discount;
        if (taxableAmount < 0) taxableAmount = 0;
        return Math.Round(taxableAmount * VatRate, 2, MidpointRounding.AwayFromZero);
    }
}
