using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>Backs the "Add Supplier" form used ahead of raising purchase orders.</summary>
public class SupplierViewModel
{
    [Required(ErrorMessage = "Supplier name is required")]
    [MaxLength(150)]
    [Display(Name = "Supplier Name")]
    public string Name { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Contact Email")]
    public string? ContactEmail { get; set; }

    [Display(Name = "Contact Phone")]
    public string? ContactPhone { get; set; }

    [Range(0, 365)]
    [Display(Name = "Typical Lead Time (days)")]
    public int LeadTimeDays { get; set; } = 7;
}
