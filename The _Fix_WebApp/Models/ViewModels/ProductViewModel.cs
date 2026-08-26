using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

public class ProductViewModel
{
    public int ProductId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000), Display(Name = "Description")]
    public string? Description { get; set; }

    [Required, MaxLength(50), Display(Name = "SKU / Barcode")]
    public string SKU { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Brand { get; set; }

    [Range(0, double.MaxValue), Display(Name = "Cost Price")]
    public decimal CostPrice { get; set; }

    [Range(0, double.MaxValue), Display(Name = "Selling Price")]
    public decimal SellingPrice { get; set; }

    [Display(Name = "Product Image")]
    public string? ImageUrl { get; set; }

    [Range(0, int.MaxValue), Display(Name = "Stock Quantity")]
    public int StockQuantity { get; set; }

    [Range(0, int.MaxValue), Display(Name = "Low Stock Threshold")]
    public int LowStockThreshold { get; set; } = 5;

    public bool IsActive { get; set; } = true;
}

/// <summary>Search/filter criteria for the master catalogue view (US-02).</summary>
public class ProductFilterViewModel
{
    public string? SearchTerm { get; set; }
    public string? Category { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public bool? InStockOnly { get; set; }
}
