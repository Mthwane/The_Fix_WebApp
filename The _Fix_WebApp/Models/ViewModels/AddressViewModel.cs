using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>Backs the "Add/Edit Address" form on the customer Profile page.</summary>
public class AddressViewModel
{
    public int CustomerAddressId { get; set; }

    [Required(ErrorMessage = "Give this address a short label, e.g. 'Home' or 'Work'.")]
    [MaxLength(50)]
    public string Label { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    [Display(Name = "Recipient Name")]
    public string RecipientName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    [Phone(ErrorMessage = "Enter a valid phone number.")]
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

    [Display(Name = "Set as my default delivery address")]
    public bool IsDefault { get; set; }
}