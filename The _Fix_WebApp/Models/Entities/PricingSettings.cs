using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// The store-wide default markup, used whenever a product's category has no
/// CategoryPricingRule of its own. Deliberately kept separate from SiteSettings (which is
/// scoped to homepage copy only, per its own doc comment) rather than bolted onto it.
/// Singleton row (Id always 1), same ValueGeneratedNever pattern as SiteSettings - see
/// ApplicationDbContext.OnModelCreating and Program.cs for the seeding precedent this follows.
/// </summary>
public class PricingSettings
{
    [Key]
    public int Id { get; set; }

    /// <summary>e.g. 60 means Selling Price = Cost Price x 1.60. Used for any category with no
    /// CategoryPricingRule override.</summary>
    [Range(0, 1000)]
    public decimal DefaultMarkupPercentage { get; set; } = 60;

    public DateTime DateUpdated { get; set; } = DateTime.UtcNow;
}
