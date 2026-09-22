using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services.Courier;

/// <summary>Config for The Courier Guy / Shiplogic. The API key is a secret - keep it in
/// user-secrets locally and an env var / key vault in production, never in appsettings.json.</summary>
public class CourierGuyOptions
{
    /// <summary>Sandbox is https://api.shiplogic.com; production is
    /// https://api.portal.thecourierguy.co.za (confirm with the courier when going live).</summary>
    public string BaseUrl { get; set; } = "https://api.shiplogic.com";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Our warehouse/store - the collection address for outbound customer deliveries
    /// and the delivery address for inbound supplier stock.</summary>
    public CourierAddressOptions StoreAddress { get; set; } = new();
    public CourierContactOptions StoreContact { get; set; } = new();

    /// <summary>Fallback service level when the caller doesn't pick one from a rates quote.</summary>
    public string DefaultServiceLevelCode { get; set; } = "ECO";

    /// <summary>Dimensions used when a parcel's real size isn't known - a single garment flyer.
    /// Deliberately conservative; under-declaring gets re-billed by the courier after they
    /// dimension the parcel at the hub.</summary>
    public double DefaultParcelLengthCm { get; set; } = 30;
    public double DefaultParcelWidthCm { get; set; } = 25;
    public double DefaultParcelHeightCm { get; set; } = 10;
    public double DefaultParcelWeightKg { get; set; } = 1;

    /// <summary>How stale tracking data may be before a refresh is attempted. Stops every page
    /// load hammering their API (and tripping the 429 rate limit).</summary>
    public int TrackingCacheMinutes { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public class CourierAddressOptions
{
    public string Company { get; set; } = "Fashion Fix";
    public string StreetAddress { get; set; } = string.Empty;
    public string LocalArea { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string Country { get; set; } = "ZA";
    public string Code { get; set; } = string.Empty;
    public double? Lat { get; set; }
    public double? Lng { get; set; }
}

public class CourierContactOptions
{
    public string Name { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Wraps The Courier Guy REST API. Every method returns a CourierResult rather than throwing:
/// a courier outage should degrade the feature (no waybill yet, stale tracking) but must never
/// break a checkout, a stock receipt, or a page load.
/// </summary>
public interface ICourierService
{
    bool IsConfigured { get; }

    /// <summary>Quote service levels + prices for a delivery, so staff/customers can choose.</summary>
    Task<CourierResult<List<CourierRate>>> GetRatesAsync(CourierAddress delivery, List<CourierParcel> parcels, decimal? declaredValue = null);

    /// <summary>Books an outbound delivery for a customer order and persists the local
    /// CourierShipment mirror. Collection = our store, delivery = the order's address snapshot.</summary>
    Task<CourierResult<CourierShipment>> CreateOrderShipmentAsync(Order order, string? serviceLevelCode = null);

    /// <summary>Books an inbound collection for an approved purchase order.
    /// Collection = the supplier's address, delivery = our store.</summary>
    Task<CourierResult<CourierShipment>> CreatePurchaseOrderShipmentAsync(PurchaseOrder purchaseOrder, string? serviceLevelCode = null);

    /// <summary>Pulls fresh tracking and appends any new events. Respects TrackingCacheMinutes
    /// unless force is set.</summary>
    Task<CourierResult<CourierShipment>> RefreshTrackingAsync(int courierShipmentId, bool force = false);

    Task<CourierResult<bool>> CancelShipmentAsync(int courierShipmentId);

    /// <summary>Signed URL to the waybill PDF (expires after 24h per their docs).</summary>
    Task<CourierResult<string>> GetLabelUrlAsync(int courierShipmentId);
}
