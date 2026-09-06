using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>View model for Shop/Department/{slug} - matches the Figma department landing
/// pages (Footwear, Men's, Women's, etc.): a hero, a sub-category pill bar, and a filtered
/// product grid.</summary>
public class DepartmentPageViewModel
{
    public Department Department { get; set; } = null!;
    public List<Product> Products { get; set; } = new();
    public string? SelectedSubCategory { get; set; }
    public string? SelectedSize { get; set; }
    public string? SelectedColor { get; set; }
}
