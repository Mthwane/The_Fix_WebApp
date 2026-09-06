using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// A vendor we buy stock from. The address fields matter beyond record-keeping: they're the
/// collection address sent to The Courier Guy when a purchase order is shipped to us, so they
/// need to be complete enough to geocode.
/// </summary>
public class Supplier
{
    [Key]
    public int SupplierId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ContactName { get; set; }

    [MaxLength(150)]
    public string? ContactEmail { get; set; }

    [MaxLength(30)]
    public string? ContactPhone { get; set; }

    /// <summary>Typical lead time in days from PO placement to delivery.</summary>
    public int LeadTimeDays { get; set; }

    // --- Collection address (used as the courier's collection point for inbound stock) ---
    [MaxLength(200)]
    public string? StreetAddress { get; set; }

    [MaxLength(100)]
    public string? LocalArea { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>Province - "zone" in Courier Guy terms (e.g. "Gauteng", "Western Cape").</summary>
    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(10)]
    public string? PostalCode { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public ICollection<RestockBundle> RestockBundles { get; set; } = new List<RestockBundle>();
}

/// <summary>
/// Lifecycle of a restock request. The extra states versus V1 exist because an order now has to
/// be authorised before any money is committed: anyone with PurchaseOrdersManage can raise a
/// Draft and submit it, but only PurchaseOrdersApprove can move it past AwaitingApproval.
/// </summary>
public enum PurchaseOrderStatus
{
    Draft,
    AwaitingApproval,
    Approved,
    Rejected,
    Shipped,
    Received,
    Cancelled
}

public class PurchaseOrder
{
    [Key]
    public int PurchaseOrderId { get; set; }

    [Required, MaxLength(30)]
    public string PONumber { get; set; } = string.Empty;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    // --- Approval gate ---
    public string? ApprovedByUserId { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
    public DateTime? DateApproved { get; set; }

    /// <summary>Set when a manager rejects the request, so the raiser knows why.</summary>
    [MaxLength(500)]
    public string? ReviewNotes { get; set; }

    /// <summary>Set when this PO was generated from a saved bundle, for traceability.</summary>
    public int? RestockBundleId { get; set; }
    public RestockBundle? RestockBundle { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? DateSubmitted { get; set; }
    public DateTime? DateExpected { get; set; }
    public DateTime? DateReceived { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();

    /// <summary>Total committed spend - what the approver is actually authorising.</summary>
    [NotMapped]
    public decimal TotalCost => Items.Sum(i => i.QuantityOrdered * i.UnitCost);

    [NotMapped]
    public bool IsEditable => Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected;
}

public class PurchaseOrderItem
{
    [Key]
    public int PurchaseOrderItemId { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    /// <summary>
    /// Restocking is per size/colour, not per style - you can't order "a hoodie", you order
    /// "M/Black hoodie". This is the single biggest change from the V1 shape, which keyed off
    /// ProductId and couldn't express which variant was actually being replenished.
    /// </summary>
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    [NotMapped]
    public int QuantityOutstanding => Math.Max(0, QuantityOrdered - QuantityReceived);
}
