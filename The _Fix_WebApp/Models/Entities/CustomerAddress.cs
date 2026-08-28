using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// A saved delivery address on a customer's account, so they pick "Home" or "Work" at
/// checkout instead of retyping their address every time. Orders snapshot these fields
/// onto themselves at the time of purchase (see Order.Delivery*), so editing or deleting
/// a saved address later never rewrites the delivery details of a past order.
/// </summary>
public class CustomerAddress
{
    [Key]
    public int CustomerAddressId { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser Customer { get; set; } = null!;

    /// <summary>Short name the customer picks it out by, e.g. "Home", "Work", "Mom's House".</summary>
    [Required, MaxLength(50)]
    public string Label { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    [Display(Name = "Recipient Name")]
    public string RecipientName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    [Display(Name = "Address Line 1")]
    public string AddressLine1 { get; set; } = string.Empty;

    [MaxLength(200)]
    [Display(Name = "Address Line 2 (optional)")]
    public string? AddressLine2 { get; set; }

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Province { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    [Display(Name = "Postal Code")]
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>The address pre-selected at checkout. Only one per customer - enforced in CustomerController.</summary>
    public bool IsDefault { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}