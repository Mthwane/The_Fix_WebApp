namespace FashionFix.Web.Services;

/// <summary>
/// A discount that passed validation moments ago could not be redeemed (its limit was reached by another sale,
/// or it was switched off). Thrown inside the order transaction so the whole sale rolls back cleanly.
/// </summary>
public class DiscountUnavailableException : Exception
{
    public string Code { get; }

    public DiscountUnavailableException(string code)
        : base($"The discount code '{code}' is no longer available.")
    {
        Code = code;
    }
}
