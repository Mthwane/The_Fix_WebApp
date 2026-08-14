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

    [Range(0, double.MaxValue)]
    public decimal TaxTotal { get; set; }

    public decimal SubTotal => CartItems.Sum(i => i.LineTotal);

    public decimal GrandTotal => SubTotal - DiscountTotal + TaxTotal;

    /// <summary>Optional - link a sale to a registered customer account.</summary>
    public string? CustomerId { get; set; }
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
