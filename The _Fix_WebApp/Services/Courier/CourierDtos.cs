using System.Text.Json.Serialization;

namespace FashionFix.Web.Services.Courier;

// ---------------------------------------------------------------------------
// DTOs for The Courier Guy / Shiplogic REST API.
// Property names are snake_case to match their JSON exactly (set explicitly via
// JsonPropertyName rather than relying on a naming policy, so a policy change
// elsewhere in the app can never silently break the contract).
// Their docs note that falsy fields may be OMITTED from responses, so every
// response field here is nullable - don't assume anything is present.
// ---------------------------------------------------------------------------

public class CourierAddress
{
    [JsonPropertyName("type")] public string? Type { get; set; } // residential | business | counter | locker
    [JsonPropertyName("company")] public string? Company { get; set; }
    [JsonPropertyName("street_address")] public string? StreetAddress { get; set; }
    [JsonPropertyName("local_area")] public string? LocalArea { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("zone")] public string? Zone { get; set; }       // province
    [JsonPropertyName("country")] public string? Country { get; set; } // "ZA"
    [JsonPropertyName("code")] public string? Code { get; set; }       // postal code
    [JsonPropertyName("lat")] public double? Lat { get; set; }
    [JsonPropertyName("lng")] public double? Lng { get; set; }
}

public class CourierContact
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("mobile_number")] public string? MobileNumber { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
}

public class CourierParcel
{
    [JsonPropertyName("parcel_description")] public string? ParcelDescription { get; set; }
    [JsonPropertyName("submitted_length_cm")] public double SubmittedLengthCm { get; set; }
    [JsonPropertyName("submitted_width_cm")] public double SubmittedWidthCm { get; set; }
    [JsonPropertyName("submitted_height_cm")] public double SubmittedHeightCm { get; set; }
    [JsonPropertyName("submitted_weight_kg")] public double SubmittedWeightKg { get; set; }
}

// --- Rates ---

public class CourierRatesRequest
{
    [JsonPropertyName("collection_address")] public CourierAddress CollectionAddress { get; set; } = new();
    [JsonPropertyName("delivery_address")] public CourierAddress DeliveryAddress { get; set; } = new();
    [JsonPropertyName("parcels")] public List<CourierParcel> Parcels { get; set; } = new();
    [JsonPropertyName("declared_value")] public decimal? DeclaredValue { get; set; }
}

public class CourierRatesResponse
{
    [JsonPropertyName("rates")] public List<CourierRate>? Rates { get; set; }
}

public class CourierRate
{
    [JsonPropertyName("rate")] public decimal? Rate { get; set; }
    [JsonPropertyName("base_rate")] public decimal? BaseRate { get; set; }
    [JsonPropertyName("service_level")] public CourierServiceLevel? ServiceLevel { get; set; }
}

public class CourierServiceLevel
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("delivery_date_from")] public DateTime? DeliveryDateFrom { get; set; }
    [JsonPropertyName("delivery_date_to")] public DateTime? DeliveryDateTo { get; set; }
}

// --- Shipments ---

public class CourierCreateShipmentRequest
{
    [JsonPropertyName("collection_address")] public CourierAddress CollectionAddress { get; set; } = new();
    [JsonPropertyName("collection_contact")] public CourierContact CollectionContact { get; set; } = new();
    [JsonPropertyName("delivery_address")] public CourierAddress DeliveryAddress { get; set; } = new();
    [JsonPropertyName("delivery_contact")] public CourierContact DeliveryContact { get; set; } = new();
    [JsonPropertyName("parcels")] public List<CourierParcel> Parcels { get; set; } = new();
    [JsonPropertyName("service_level_code")] public string? ServiceLevelCode { get; set; }
    [JsonPropertyName("declared_value")] public decimal? DeclaredValue { get; set; }

    /// <summary>Our order/PO number, so their portal shows a reference we recognise.</summary>
    [JsonPropertyName("customer_reference")] public string? CustomerReference { get; set; }
    [JsonPropertyName("customer_reference_name")] public string? CustomerReferenceName { get; set; }

    [JsonPropertyName("special_instructions_collection")] public string? SpecialInstructionsCollection { get; set; }
    [JsonPropertyName("special_instructions_delivery")] public string? SpecialInstructionsDelivery { get; set; }
    [JsonPropertyName("collection_min_date")] public DateTime? CollectionMinDate { get; set; }
    [JsonPropertyName("mute_notifications")] public bool MuteNotifications { get; set; }
}

public class CourierShipmentResponse
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("short_tracking_reference")] public string? ShortTrackingReference { get; set; }
    [JsonPropertyName("custom_tracking_reference")] public string? CustomTrackingReference { get; set; }
    [JsonPropertyName("customer_reference")] public string? CustomerReference { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("service_level_code")] public string? ServiceLevelCode { get; set; }
    [JsonPropertyName("service_level_name")] public string? ServiceLevelName { get; set; }
    [JsonPropertyName("rate")] public decimal? Rate { get; set; }
    [JsonPropertyName("estimated_collection")] public DateTime? EstimatedCollection { get; set; }
    [JsonPropertyName("estimated_delivery_from")] public DateTime? EstimatedDeliveryFrom { get; set; }
    [JsonPropertyName("estimated_delivery_to")] public DateTime? EstimatedDeliveryTo { get; set; }
    [JsonPropertyName("collected_date")] public DateTime? CollectedDate { get; set; }
    [JsonPropertyName("delivered_date")] public DateTime? DeliveredDate { get; set; }
}

// --- Tracking ---

public class CourierTrackingResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("shipment_collected_date")] public DateTime? ShipmentCollectedDate { get; set; }
    [JsonPropertyName("shipment_delivered_date")] public DateTime? ShipmentDeliveredDate { get; set; }
    [JsonPropertyName("shipment_estimated_delivery_from")] public DateTime? EstimatedDeliveryFrom { get; set; }
    [JsonPropertyName("shipment_estimated_delivery_to")] public DateTime? EstimatedDeliveryTo { get; set; }
    [JsonPropertyName("tracking_events")] public List<CourierTrackingEventDto>? TrackingEvents { get; set; }
}

public class CourierTrackingEventDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("date")] public DateTime Date { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }

    /// <summary>0 = the event refers to the shipment; &gt;0 = a specific parcel within it.</summary>
    [JsonPropertyName("parcel_id")] public long ParcelId { get; set; }
}

public class CourierCancelRequest
{
    [JsonPropertyName("tracking_reference")] public string TrackingReference { get; set; } = string.Empty;
}

/// <summary>Uniform result wrapper so callers never have to catch HTTP exceptions - a courier
/// outage must never take down a checkout or a stock receipt.</summary>
public class CourierResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }

    public static CourierResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static CourierResult<T> Fail(string message) => new() { Success = false, ErrorMessage = message };
}
