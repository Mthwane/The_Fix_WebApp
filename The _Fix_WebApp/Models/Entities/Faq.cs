using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>A single question/answer pair on the public FAQ page (US-support-1).</summary>
public class Faq
{
    [Key]
    public int FaqId { get; set; }

    [Required, MaxLength(300)]
    public string Question { get; set; } = string.Empty;

    [Required]
    public string Answer { get; set; } = string.Empty;

    /// <summary>Free-text grouping shown as a section heading on the public page (e.g.
    /// "Orders & Delivery", "Returns & Refunds", "Payments", "Account"). Not an enum - staff
    /// type the category when creating/editing an FAQ, and the public page groups by whatever
    /// distinct values currently exist, so a new category needs no code change.</summary>
    [Required, MaxLength(60)]
    public string Category { get; set; } = "General";

    /// <summary>Lower sorts first, both within a category and (by its lowest member) between
    /// categories.</summary>
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}
