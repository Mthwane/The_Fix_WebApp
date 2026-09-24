using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FashionFix.Web.Services.Courier;

public class EasyPostOptions
{
    /// <summary>A "test" key (starts with EZTK) never charges and never creates a real
    /// shipment - see https://www.easypost.com/docs/api#authentication. A "production" key
    /// (EZAK) would actually buy real postage, so don't put one here.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TrackingCacheMinutes { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// A second, REAL ICourierService implementation - unlike FakeCourierService (which never
/// leaves the process), this genuinely calls the EasyPost API over HTTP, for when you want to
/// exercise real network/JSON handling without spending real (or sandbox) courier balance.
/// EasyPost's test mode is free with no card required and never charges - see
/// https://www.easypost.com/docs/api#test-vs-production-mode.
///
/// Two honest limitations, both acceptable for a testing-only adapter (see Program.cs comment -
/// this is never selectable outside Development):
///  1. EasyPost shipment IDs are opaque strings ("shp_..."), but CourierShipment.ProviderShipmentId
///     is a long (built for Shiplogic's numeric IDs). Rather than change a shared entity used by
///     the real production service too, this just doesn't persist it - tracking refresh uses the
///     tracking_code instead (EasyPost supports looking up a Tracker by tracking_code + carrier,
///     no shipment id needed), which is all the rest of the app actually needs.
///  2. Re-fetching a label after creation, and calling EasyPost's real refund API on cancel,
///     would also need that persisted shipment id. CancelShipmentAsync below is local-only as a
///     result, and GetLabelUrlAsync points you at the server log/EasyPost dashboard instead -
///     the label URL returned at booking time is logged there.
/// </summary>
public class EasyPostCourierService : ICourierService
{
    private const double CmToIn = 0.393701;
    private const double KgToOz = 35.274;

    private readonly HttpClient _http;
    private readonly ApplicationDbContext _context;
    private readonly EasyPostOptions _options;
    private readonly CourierGuyOptions _storeOptions; // reused purely for the store's own address/contact/parcel defaults
    private readonly ILogger<EasyPostCourierService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EasyPostCourierService(
        HttpClient http,
        ApplicationDbContext context,
        IOptions<EasyPostOptions> options,
        IOptions<CourierGuyOptions> storeOptions,
        ILogger<EasyPostCourierService> logger)
    {
        _options = options.Value;
        _storeOptions = storeOptions.Value;
        _context = context;
        _logger = logger;

        _http = http;
        _http.BaseAddress = new Uri("https://api.easypost.com/v2/");
        if (_options.IsConfigured)
        {
            // Basic auth: API key as the username, blank password - not Bearer.
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(_options.ApiKey + ":"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    public bool IsConfigured => _options.IsConfigured;

    // ===================== Rates =====================

    public async Task<CourierResult<List<CourierRate>>> GetRatesAsync(CourierAddress delivery, List<CourierParcel> parcels, decimal? declaredValue = null)
    {
        if (!IsConfigured) return CourierResult<List<CourierRate>>.Fail("EasyPost API is not configured.");

        var parcel = parcels.FirstOrDefault();
        var request = new EasyPostShipmentCreateEnvelope
        {
            Shipment = new EasyPostShipmentCreateRequest
            {
                ToAddress = ToEasyPostAddress(delivery, null, null),
                FromAddress = StoreAddress(),
                Parcel = ToEasyPostParcel(parcel)
            }
        };

        var result = await PostAsync<EasyPostShipmentCreateEnvelope, EasyPostShipmentResponse>("shipments", request);
        if (!result.Success) return CourierResult<List<CourierRate>>.Fail(result.ErrorMessage!);

        var rates = (result.Data!.Rates)
            .Select(r => new CourierRate
            {
                Rate = ParseDecimal(r.Rate),
                BaseRate = ParseDecimal(r.Rate),
                ServiceLevel = new CourierServiceLevel
                {
                    Code = r.Service,
                    Name = $"{r.Carrier} {r.Service}",
                    DeliveryDateFrom = r.DeliveryDate,
                    DeliveryDateTo = r.DeliveryDate
                }
            })
            .ToList();

        return CourierResult<List<CourierRate>>.Ok(rates);
    }

    // ===================== Create shipments =====================

    public async Task<CourierResult<CourierShipment>> CreateOrderShipmentAsync(Order order, string? serviceLevelCode = null)
    {
        if (!IsConfigured) return CourierResult<CourierShipment>.Fail("EasyPost API is not configured.");

        if (string.IsNullOrWhiteSpace(order.DeliveryAddressLine1))
            return CourierResult<CourierShipment>.Fail("This order has no delivery address (in-store sales aren't shipped).");

        var existing = await _context.CourierShipments.FirstOrDefaultAsync(s => s.OrderId == order.OrderId && s.Status != "cancelled");
        if (existing is not null)
            return CourierResult<CourierShipment>.Fail($"Order {order.OrderNumber} already has waybill {existing.TrackingReference}.");

        var toAddress = new EasyPostAddressDto
        {
            Name = order.DeliveryRecipientName,
            Street1 = order.DeliveryAddressLine1,
            Street2 = order.DeliveryAddressLine2,
            City = order.DeliveryCity,
            State = order.DeliveryProvince,
            Zip = order.DeliveryPostalCode,
            Country = "ZA",
            Phone = order.DeliveryPhoneNumber,
            Email = order.Customer?.Email
        };

        return await CreateShipmentAsync(toAddress, StoreAddress(), order.OrderId, null, order.OrderNumber, serviceLevelCode);
    }

    public async Task<CourierResult<CourierShipment>> CreatePurchaseOrderShipmentAsync(PurchaseOrder purchaseOrder, string? serviceLevelCode = null)
    {
        if (!IsConfigured) return CourierResult<CourierShipment>.Fail("EasyPost API is not configured.");

        var supplier = purchaseOrder.Supplier;
        if (supplier is null || string.IsNullOrWhiteSpace(supplier.StreetAddress))
            return CourierResult<CourierShipment>.Fail("Supplier has no collection address on file - add one before booking a collection.");

        var existing = await _context.CourierShipments.FirstOrDefaultAsync(s => s.PurchaseOrderId == purchaseOrder.PurchaseOrderId && s.Status != "cancelled");
        if (existing is not null)
            return CourierResult<CourierShipment>.Fail($"{purchaseOrder.PONumber} already has waybill {existing.TrackingReference}.");

        // Inbound stock: collection is the SUPPLIER, delivery is US - the reverse of a customer order.
        var fromAddress = new EasyPostAddressDto
        {
            Name = supplier.ContactName ?? supplier.Name,
            Company = supplier.Name,
            Street1 = supplier.StreetAddress,
            Street2 = supplier.LocalArea,
            City = supplier.City,
            State = supplier.Province,
            Zip = supplier.PostalCode,
            Country = "ZA",
            Phone = supplier.ContactPhone,
            Email = supplier.ContactEmail
        };

        return await CreateShipmentAsync(StoreAddress(), fromAddress, null, purchaseOrder.PurchaseOrderId, purchaseOrder.PONumber, serviceLevelCode);
    }

    private async Task<CourierResult<CourierShipment>> CreateShipmentAsync(
        EasyPostAddressDto toAddress, EasyPostAddressDto fromAddress,
        int? orderId, int? purchaseOrderId, string customerReference, string? serviceLevelCode)
    {
        var createRequest = new EasyPostShipmentCreateEnvelope
        {
            Shipment = new EasyPostShipmentCreateRequest
            {
                ToAddress = toAddress,
                FromAddress = fromAddress,
                Parcel = ToEasyPostParcel(null)
            }
        };

        var created = await PostAsync<EasyPostShipmentCreateEnvelope, EasyPostShipmentResponse>("shipments", createRequest);
        if (!created.Success) return CourierResult<CourierShipment>.Fail(created.ErrorMessage!);

        var shipmentId = created.Data!.Id;
        var rates = created.Data!.Rates;
        if (rates.Count == 0)
            return CourierResult<CourierShipment>.Fail("EasyPost returned no rates for this address/parcel - nothing to buy.");

        // Prefer a rate matching the requested service (by carrier or service code); otherwise
        // the cheapest one, same "lowest rate" convention EasyPost's own SDKs use by default.
        var chosen = (serviceLevelCode is null ? null : rates.FirstOrDefault(r =>
                string.Equals(r.Service, serviceLevelCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Carrier, serviceLevelCode, StringComparison.OrdinalIgnoreCase)))
            ?? rates.OrderBy(r => ParseDecimal(r.Rate)).First();

        var buyRequest = new EasyPostBuyEnvelope { Rate = new EasyPostBuyRateRef { Id = chosen.Id } };
        var bought = await PostAsync<EasyPostBuyEnvelope, EasyPostShipmentResponse>($"shipments/{shipmentId}/buy", buyRequest);
        if (!bought.Success) return CourierResult<CourierShipment>.Fail(bought.ErrorMessage!);

        var payload = bought.Data!;
        var now = DateTime.UtcNow;
        var shipment = new CourierShipment
        {
            OrderId = orderId,
            PurchaseOrderId = purchaseOrderId,
            ProviderShipmentId = 0, // EasyPost's real id ("shp_...") doesn't fit this long field - see class comment
            TrackingReference = payload.TrackingCode ?? string.Empty,
            CustomerReference = customerReference,
            Status = "submitted",
            ServiceLevelCode = payload.SelectedRate?.Carrier ?? chosen.Carrier, // carrier, not a Shiplogic-style code - needed to look the tracker back up later
            ServiceLevelName = $"{payload.SelectedRate?.Carrier ?? chosen.Carrier} {payload.SelectedRate?.Service ?? chosen.Service}",
            Rate = ParseDecimal(payload.SelectedRate?.Rate ?? chosen.Rate),
            EstimatedDeliveryFrom = payload.SelectedRate?.DeliveryDate ?? chosen.DeliveryDate,
            EstimatedDeliveryTo = payload.SelectedRate?.DeliveryDate ?? chosen.DeliveryDate,
            LastSyncedAt = now
        };
        shipment.TrackingEvents.Add(new CourierTrackingEvent
        {
            ProviderEventId = 0,
            Status = "submitted",
            Message = "Shipment purchased via EasyPost (test mode).",
            EventDate = now
        });

        _context.CourierShipments.Add(shipment);
        await _context.SaveChangesAsync();

        // The label URL isn't persisted anywhere on CourierShipment (see class comment) - this
        // is the only place you can get it (and the public tracking page) back short of the
        // EasyPost dashboard.
        _logger.LogInformation("[EasyPost] Bought shipment {Ref} via {Carrier} {Service} - label: {Label} - track: {Track}",
            shipment.TrackingReference, shipment.ServiceLevelCode, shipment.ServiceLevelName,
            payload.PostageLabel?.LabelUrl, payload.Tracker?.PublicUrl);

        return CourierResult<CourierShipment>.Ok(shipment);
    }

    // ===================== Tracking =====================

    public async Task<CourierResult<CourierShipment>> RefreshTrackingAsync(int courierShipmentId, bool force = false)
    {
        var shipment = await _context.CourierShipments
            .Include(s => s.TrackingEvents)
            .FirstOrDefaultAsync(s => s.CourierShipmentId == courierShipmentId);

        if (shipment is null) return CourierResult<CourierShipment>.Fail("Shipment not found.");
        if (!IsConfigured) return CourierResult<CourierShipment>.Fail("EasyPost API is not configured.");

        if (!force && (shipment.IsComplete ||
            (shipment.LastSyncedAt.HasValue && shipment.LastSyncedAt.Value.AddMinutes(_options.TrackingCacheMinutes) > DateTime.UtcNow)))
        {
            return CourierResult<CourierShipment>.Ok(shipment);
        }

        // Creating a tracker for a tracking_code/carrier pair that already exists just returns
        // the existing one - this doubles as both "create" and "fetch" here.
        var request = new EasyPostTrackerEnvelope
        {
            Tracker = new EasyPostTrackerCreateRequest { TrackingCode = shipment.TrackingReference, Carrier = shipment.ServiceLevelCode }
        };
        var result = await PostAsync<EasyPostTrackerEnvelope, EasyPostTrackerResponse>("trackers", request);
        if (!result.Success) return CourierResult<CourierShipment>.Fail(result.ErrorMessage!);

        var tracker = result.Data!;
        if (!string.IsNullOrWhiteSpace(tracker.Status)) shipment.Status = MapTrackerStatus(tracker.Status);
        shipment.LastSyncedAt = DateTime.UtcNow;
        if (shipment.Status == "delivered") shipment.DeliveredDate ??= DateTime.UtcNow;

        // De-duped on message+status+timestamp, since EasyPost's tracking_details don't carry a
        // stable per-event id the way Shiplogic's events do.
        var known = shipment.TrackingEvents.Select(e => (e.Status, e.Message, e.EventDate)).ToHashSet();
        foreach (var ev in tracker.TrackingDetails)
        {
            var eventDate = ev.DateTime ?? DateTime.UtcNow;
            var key = (ev.Status ?? "", ev.Message, eventDate);
            if (known.Contains(key)) continue;

            shipment.TrackingEvents.Add(new CourierTrackingEvent
            {
                ProviderEventId = 0,
                Status = ev.Status ?? string.Empty,
                Message = ev.Message,
                Location = ev.Location is null ? null : string.Join(", ", new[] { ev.Location.City, ev.Location.State, ev.Location.Country }.Where(s => !string.IsNullOrWhiteSpace(s))),
                EventDate = eventDate
            });
        }

        await _context.SaveChangesAsync();
        return CourierResult<CourierShipment>.Ok(shipment);
    }

    // Translates EasyPost's tracker status vocabulary into the raw strings CourierShipment.Stage
    // already understands (Shiplogic's vocabulary, since that's this app's canonical one). Only
    // the ones that don't already have an equivalent term need mapping - anything left
    // unmapped safely falls through to Stage's own "In Transit" default.
    private static string MapTrackerStatus(string easyPostStatus) => easyPostStatus switch
    {
        "pre_transit" or "unknown" => "submitted", // "unknown" is EasyPost's real initial status right after buying, before any carrier scan
        "return_to_sender" => "returned-to-sender",
        "failure" => "undeliverable",
        _ => easyPostStatus // delivered/cancelled match Shiplogic's own terms exactly already
    };

    // ===================== Cancel / label =====================

    public async Task<CourierResult<bool>> CancelShipmentAsync(int courierShipmentId)
    {
        // Local-only (see class comment) - doesn't call EasyPost's refund API, since that needs
        // the shipment id this app doesn't persist. Fine for a testing-only adapter: it's test
        // money either way.
        var shipment = await _context.CourierShipments.FindAsync(courierShipmentId);
        if (shipment is null) return CourierResult<bool>.Fail("Shipment not found.");

        shipment.Status = "cancelled";
        shipment.LastSyncedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return CourierResult<bool>.Ok(true);
    }

    public Task<CourierResult<string>> GetLabelUrlAsync(int courierShipmentId)
        => Task.FromResult(CourierResult<string>.Fail(
            "Label URL isn't persisted for EasyPost test shipments - check the server log line logged " +
            "at booking time (search \"[EasyPost] Bought shipment\", which also has the public tracking " +
            "page URL), or the EasyPost dashboard."));

    // ===================== Mapping helpers =====================

    private EasyPostAddressDto StoreAddress() => new()
    {
        Name = _storeOptions.StoreContact.Name,
        Company = _storeOptions.StoreAddress.Company,
        Street1 = _storeOptions.StoreAddress.StreetAddress,
        Street2 = _storeOptions.StoreAddress.LocalArea,
        City = _storeOptions.StoreAddress.City,
        State = _storeOptions.StoreAddress.Zone,
        Zip = _storeOptions.StoreAddress.Code,
        Country = string.IsNullOrWhiteSpace(_storeOptions.StoreAddress.Country) ? "ZA" : _storeOptions.StoreAddress.Country,
        Phone = _storeOptions.StoreContact.MobileNumber,
        Email = _storeOptions.StoreContact.Email
    };

    private static EasyPostAddressDto ToEasyPostAddress(CourierAddress a, string? name, string? phone) => new()
    {
        Name = name,
        Company = a.Company,
        Street1 = a.StreetAddress,
        Street2 = a.LocalArea,
        City = a.City,
        State = a.Zone,
        Zip = a.Code,
        Country = string.IsNullOrWhiteSpace(a.Country) ? "ZA" : a.Country,
        Phone = phone
    };

    private EasyPostParcelDto ToEasyPostParcel(CourierParcel? p) => new()
    {
        Length = (p?.SubmittedLengthCm ?? _storeOptions.DefaultParcelLengthCm) * CmToIn,
        Width = (p?.SubmittedWidthCm ?? _storeOptions.DefaultParcelWidthCm) * CmToIn,
        Height = (p?.SubmittedHeightCm ?? _storeOptions.DefaultParcelHeightCm) * CmToIn,
        Weight = (p?.SubmittedWeightKg ?? _storeOptions.DefaultParcelWeightKg) * KgToOz
    };

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;

    // ===================== HTTP plumbing (same shape as CourierGuyService) =====================

    private async Task<CourierResult<TResponse>> PostAsync<TRequest, TResponse>(string path, TRequest body)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(path, body, JsonOpts);
            var raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EasyPost API returned {Status}: {Body}", (int)response.StatusCode, raw);
                return CourierResult<TResponse>.Fail($"EasyPost error ({(int)response.StatusCode}): {DescribeError(raw)}");
            }

            var data = JsonSerializer.Deserialize<TResponse>(raw, JsonOpts);
            return data is null
                ? CourierResult<TResponse>.Fail("EasyPost returned an empty response.")
                : CourierResult<TResponse>.Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EasyPost POST {Path} failed.", path);
            return CourierResult<TResponse>.Fail("Could not reach EasyPost. Try again shortly.");
        }
    }

    /// <summary>EasyPost's documented error shape is {code, message, errors: [...]} where each
    /// entry in "errors" can be either a FieldError object ({field, message, suggestion}) or a
    /// plain string - this pulls out the human-readable message from whichever shape shows up,
    /// falling back to the raw body if nothing parses (e.g. a non-JSON 5xx from a proxy).</summary>
    private static string DescribeError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            // The docs show {code, message, errors} at the root; some EasyPost error responses
            // wrap this under an "error" key instead - check root first, fall back to that.
            var root = doc.RootElement;
            var error = root.TryGetProperty("error", out var wrapped) ? wrapped : root;

            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;

            var fieldMessages = new List<string>();
            if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in errors.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                        fieldMessages.Add(entry.GetString() ?? "");
                    else if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("message", out var em))
                        fieldMessages.Add(em.GetString() ?? "");
                }
            }

            var suffix = fieldMessages.Count > 0 ? " - " + string.Join("; ", fieldMessages.Where(s => !string.IsNullOrWhiteSpace(s))) : "";
            return (message ?? raw.Trim()) + suffix;
        }
        catch (JsonException)
        {
            return raw.Trim();
        }
    }
}

// ===================== Wire DTOs (EasyPost's own snake_case JSON shape) =====================
// Best-effort against EasyPost's documented schema - if a field doesn't populate as expected,
// check a raw response body (logged on parse failure) against their current API docs.

internal class EasyPostAddressDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("company")] public string? Company { get; set; }
    [JsonPropertyName("street1")] public string? Street1 { get; set; }
    [JsonPropertyName("street2")] public string? Street2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("zip")] public string? Zip { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
}

internal class EasyPostParcelDto
{
    [JsonPropertyName("length")] public double Length { get; set; } // inches
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
    [JsonPropertyName("weight")] public double Weight { get; set; } // ounces
}

internal class EasyPostShipmentCreateRequest
{
    [JsonPropertyName("to_address")] public EasyPostAddressDto ToAddress { get; set; } = new();
    [JsonPropertyName("from_address")] public EasyPostAddressDto FromAddress { get; set; } = new();
    [JsonPropertyName("parcel")] public EasyPostParcelDto Parcel { get; set; } = new();
}

internal class EasyPostShipmentCreateEnvelope
{
    [JsonPropertyName("shipment")] public EasyPostShipmentCreateRequest Shipment { get; set; } = new();
}

internal class EasyPostRateDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("carrier")] public string Carrier { get; set; } = string.Empty;
    [JsonPropertyName("service")] public string Service { get; set; } = string.Empty;
    [JsonPropertyName("rate")] public string Rate { get; set; } = "0";
    [JsonPropertyName("delivery_days")] public int? DeliveryDays { get; set; }
    [JsonPropertyName("delivery_date")] public DateTime? DeliveryDate { get; set; }
}

internal class EasyPostPostageLabelDto
{
    [JsonPropertyName("label_url")] public string? LabelUrl { get; set; }
}

internal class EasyPostEmbeddedTrackerDto
{
    [JsonPropertyName("public_url")] public string? PublicUrl { get; set; }
}

internal class EasyPostShipmentResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("rates")] public List<EasyPostRateDto> Rates { get; set; } = new();
    [JsonPropertyName("tracking_code")] public string? TrackingCode { get; set; }
    [JsonPropertyName("selected_rate")] public EasyPostRateDto? SelectedRate { get; set; }
    [JsonPropertyName("postage_label")] public EasyPostPostageLabelDto? PostageLabel { get; set; }
    [JsonPropertyName("tracker")] public EasyPostEmbeddedTrackerDto? Tracker { get; set; }
}

internal class EasyPostBuyRateRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}

internal class EasyPostBuyEnvelope
{
    [JsonPropertyName("rate")] public EasyPostBuyRateRef Rate { get; set; } = new();
}

internal class EasyPostTrackerCreateRequest
{
    [JsonPropertyName("tracking_code")] public string TrackingCode { get; set; } = string.Empty;
    [JsonPropertyName("carrier")] public string? Carrier { get; set; }
}

internal class EasyPostTrackerEnvelope
{
    [JsonPropertyName("tracker")] public EasyPostTrackerCreateRequest Tracker { get; set; } = new();
}

internal class EasyPostTrackingLocationDto
{
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

internal class EasyPostTrackingDetailDto
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("datetime")] public DateTime? DateTime { get; set; }
    [JsonPropertyName("tracking_location")] public EasyPostTrackingLocationDto? Location { get; set; }
}

internal class EasyPostTrackerResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("carrier")] public string? Carrier { get; set; }
    [JsonPropertyName("tracking_details")] public List<EasyPostTrackingDetailDto> TrackingDetails { get; set; } = new();
}
