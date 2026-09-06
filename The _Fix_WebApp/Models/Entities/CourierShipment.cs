using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Our local mirror of a shipment created at The Courier Guy (Shiplogic). We store the tracking
/// reference and the last known status so staff screens can render without an API round trip on
/// every page load; the authoritative state always lives at the courier, refreshed via
/// ICourierService.RefreshTrackingAsync or an inbound webhook.
///
/// One shipment belongs to EITHER a customer Order (outbound delivery) or a PurchaseOrder
/// (inbound stock from a supplier) - never both. Both FKs are nullable for that reason.
/// </summary>
public class CourierShipment
{
    [Key]
    public int CourierShipmentId { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    /// <summary>Shiplogic's numeric shipment id - needed for the label/sticker endpoints, which
    /// take an id rather than a tracking reference.</summary>
    public long ProviderShipmentId { get; set; }

    /// <summary>The short tracking reference (e.g. "G9G"), used by /tracking/shipments and
    /// /shipments/cancel.</summary>
    [MaxLength(60)]
    public string TrackingReference { get; set; } = string.Empty;

    /// <summary>Our own reference echoed back by the courier - we send the order/PO number as
    /// customer_reference so their portal shows something we recognise.</summary>
    [MaxLength(60)]
    public string? CustomerReference { get; set; }

    /// <summary>Courier Guy status slug, verbatim: "submitted", "collected", "in-transit",
    /// "out-for-delivery", "delivered", "cancelled", etc. Stored as the raw string rather than a
    /// local enum so a new status they add never silently maps to the wrong thing.</summary>
    [MaxLength(50)]
    public string Status { get; set; } = "submitted";

    [MaxLength(30)]
    public string? ServiceLevelCode { get; set; }

    [MaxLength(100)]
    public string? ServiceLevelName { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Rate { get; set; }

    public DateTime? EstimatedCollection { get; set; }
    public DateTime? EstimatedDeliveryFrom { get; set; }
    public DateTime? EstimatedDeliveryTo { get; set; }
    public DateTime? CollectedDate { get; set; }
    public DateTime? DeliveredDate { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    /// <summary>When we last pulled fresh tracking from the courier - drives the "synced N mins
    /// ago" indicator and stops the polling loop hammering their API.</summary>
    public DateTime? LastSyncedAt { get; set; }

    public ICollection<CourierTrackingEvent> TrackingEvents { get; set; } = new List<CourierTrackingEvent>();

    [NotMapped]
    public bool IsComplete => Status is "delivered" or "cancelled" or "returned-to-sender" or "undeliverable";

    /// <summary>Maps the courier's many granular statuses onto the four stages a human cares
    /// about, for progress indicators. Anything unrecognised falls back to In Transit rather
    /// than throwing, since their status list can grow without notice.</summary>
    [NotMapped]
    public string Stage => Status switch
    {
        "submitted" or "collection-assigned" or "collection-unassigned" or "collection-rejected"
            or "awaiting-dropoff" or "collection-exception" or "collection-failed-attempt" => "Awaiting Collection",
        "collected" => "Collected",
        "delivered" or "collected-from-locker" or "collected-from-counter" => "Delivered",
        "cancelled" or "returned-to-sender" or "undeliverable" => "Closed",
        _ => "In Transit"
    };
}

/// <summary>One entry in a shipment's tracking history, as returned by /tracking/shipments.</summary>
public class CourierTrackingEvent
{
    [Key]
    public int CourierTrackingEventId { get; set; }

    public int CourierShipmentId { get; set; }
    public CourierShipment CourierShipment { get; set; } = null!;

    /// <summary>Shiplogic's own event id - used to de-duplicate on refresh so repeated polls
    /// don't pile up the same events.</summary>
    public long ProviderEventId { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Message { get; set; }

    [MaxLength(120)]
    public string? Location { get; set; }

    public DateTime EventDate { get; set; }
}
