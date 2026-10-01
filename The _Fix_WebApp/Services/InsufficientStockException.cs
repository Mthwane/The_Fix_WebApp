namespace FashionFix.Web.Services;

/// <summary>
/// Thrown when a stock decrement would take a variant below zero. Derives from InvalidOperationException
/// so existing "catch (InvalidOperationException)" handling keeps working, but lets callers treat a real
/// stock shortage (retrying can't help - tell the customer/cashier) differently from a transient
/// database fault (a retry is worth a go).
/// </summary>
public class InsufficientStockException : InvalidOperationException
{
    public int VariantId { get; }
    public string Label { get; }
    public int Requested { get; }
    public int Available { get; }

    public InsufficientStockException(int variantId, string label, int requested, int available)
        : base($"Cannot decrement stock for '{label}' below zero (have {available}, need {requested}).")
    {
        VariantId = variantId;
        Label = label;
        Requested = requested;
        Available = available;
    }
}
