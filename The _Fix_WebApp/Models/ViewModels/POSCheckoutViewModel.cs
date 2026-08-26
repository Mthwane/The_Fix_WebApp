using System.ComponentModel.DataAnnotations;
using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

public class POSCheckoutViewModel
{
    public List<POSCartLineViewModel> CartItems { get; set; } = new();

    [Required]
    public PaymentMethod PaymentMethod { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountTotal { get; set; }

    public decimal SubTotal => CartItems.Sum(i => i.LineTotal);

    /// <summary>
    /// VAT is never trusted from client input - it's always recalculated server-side from
    /// TaxSettings.VatRate at checkout time (see PosController.Checkout). This property only
    /// exists so a GrandTotal preview can be shown before the DB round-trip; nothing posts to it.
    /// </summary>
    public decimal TaxTotal { get; set; }

    public decimal GrandTotal => SubTotal - DiscountTotal + TaxTotal;

    /// <summary>Optional - link a sale to a registered customer account.</summary>
    public string? CustomerId { get; set; }

    /// <summary>Optional - email the receipt to any address, independent of CustomerId
    /// (covers walk-in customers with no account). If both are set, this one takes priority.</summary>
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email receipt to")]
    public string? ReceiptEmail { get; set; }
}

public class POSCartLineViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
