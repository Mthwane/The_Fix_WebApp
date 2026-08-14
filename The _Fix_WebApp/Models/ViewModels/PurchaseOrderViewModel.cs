using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>Backs the "Create Purchase Order" form (US-20).</summary>
public class PurchaseOrderViewModel
{
    [Required(ErrorMessage = "Please select a supplier")]
    [Display(Name = "Supplier")]
    public int SupplierId { get; set; }

    [Display(Name = "Expected Delivery Date")]
    [DataType(DataType.Date)]
    public DateTime? DateExpected { get; set; }

    public List<PurchaseOrderLineViewModel> Lines { get; set; } = new() { new PurchaseOrderLineViewModel() };
}

public class PurchaseOrderLineViewModel
{
    [Required(ErrorMessage = "Select a product")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
    public int QuantityOrdered { get; set; } = 1;

    [Range(0, double.MaxValue)]
    [Display(Name = "Unit Cost")]
    public decimal UnitCost { get; set; }
}
