using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

public enum RestockSeason
{
    AnySeason,
    Spring,
    Summer,
    Autumn,
    Winter
}

/// <summary>
/// A saved, reusable restock template - e.g. "Winter Outerwear Refill" or "Monthly Basics Top-Up".
/// A manager builds it once (which variants, how many of each, from which supplier), then generates
/// a Purchase Order from it in one click whenever it's needed, optionally overriding quantities.
///
/// The bundle is a TEMPLATE, never a live order: generating from it always creates a separate
/// PurchaseOrder that still has to go through the normal approval gate. Editing a bundle later
/// never retroactively changes purchase orders already raised from it.
/// </summary>
public class RestockBundle
{
    [Key]
    public int RestockBundleId { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public RestockSeason Season { get; set; } = RestockSeason.AnySeason;

    /// <summary>Default supplier for orders generated from this bundle. Nullable so a bundle can
    /// span suppliers, in which case the raiser picks one at generation time.</summary>
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Optional scoping labels, purely for filtering/organising bundles in the UI -
    /// the actual items are whatever variants were added, regardless of these.</summary>
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    [MaxLength(50)]
    public string? Brand { get; set; }

    public bool IsActive { get; set; } = true;

    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? DateUpdated { get; set; }

    public ICollection<RestockBundleItem> Items { get; set; } = new List<RestockBundleItem>();
    public ICollection<PurchaseOrder> GeneratedPurchaseOrders { get; set; } = new List<PurchaseOrder>();

    [NotMapped]
    public int TotalUnits => Items.Sum(i => i.DefaultQuantity);

    [NotMapped]
    public decimal EstimatedCost => Items.Sum(i => i.DefaultQuantity * (i.DefaultUnitCost ?? 0));
}

public class RestockBundleItem
{
    [Key]
    public int RestockBundleItemId { get; set; }

    public int RestockBundleId { get; set; }
    public RestockBundle RestockBundle { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    /// <summary>How many of this variant the bundle orders by default. The manager can override
    /// per-line at generation time without changing the saved template.</summary>
    public int DefaultQuantity { get; set; } = 1;

    /// <summary>Last known unit cost, used for the estimate. Null means "look it up at generation
    /// time from the variant's current cost price" rather than pinning a possibly-stale figure.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? DefaultUnitCost { get; set; }

    public int DisplayOrder { get; set; }
}
