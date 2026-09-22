using System.ComponentModel.DataAnnotations;
using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>One line in a customer's in-progress shopping cart, kept in Session (not the DB)
/// until checkout actually creates an Order - so browsing never touches stock or the database.</summary>
public class CartLineViewModel
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class CartViewModel
{
    public List<CartLineViewModel> Lines { get; set; } = new();
    public decimal SubTotal => Lines.Sum(l => l.LineTotal);
    public int ItemCount => Lines.Sum(l => l.Quantity);
}

/// <summary>Backs the checkout confirmation step (US-14).</summary>
public class CheckoutViewModel
{
    [Required(ErrorMessage = "Please choose a payment method")]
    [Display(Name = "Payment Method")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CreditCard;

    [Required(ErrorMessage = "Please choose where this order should be delivered.")]
    [Display(Name = "Deliver To")]
    public int? SelectedAddressId { get; set; }

    /// <summary>A saved Paystack card to charge directly. Null = pay with a new card via Paystack's page.</summary>
    [Display(Name = "Pay With")]
    public int? SelectedPaymentMethodId { get; set; }

    /// <summary>Only relevant when paying with a brand-new card (SelectedPaymentMethodId is null).</summary>
    [Display(Name = "Save this card for next time")]
    public bool SaveCard { get; set; } = true;

    public CartViewModel Cart { get; set; } = new();
    public List<CustomerAddress> Addresses { get; set; } = new();
    public List<CustomerPaymentMethod> SavedCards { get; set; } = new();
}