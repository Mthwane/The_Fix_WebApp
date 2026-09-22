using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FashionFix.Web.Services.Courier;

public class CourierGuyService : ICourierService
{
    private readonly HttpClient _http;
    private readonly ApplicationDbContext _context;
    private readonly CourierGuyOptions _options;
    private readonly ILogger<CourierGuyService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public CourierGuyService(
        HttpClient http,
        ApplicationDbContext context,
        IOptions<CourierGuyOptions> options,
        ILogger<CourierGuyService> logger)
    {
        _options = options.Value;
        _context = context;
        _logger = logger;

        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (_options.IsConfigured)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public bool IsConfigured => _options.IsConfigured;

    // ===================== Rates =====================

    public async Task<CourierResult<List<CourierRate>>> GetRatesAsync(CourierAddress delivery, List<CourierParcel> parcels, decimal? declaredValue = null)
    {
        if (!IsConfigured) return CourierResult<List<CourierRate>>.Fail("Courier API is not configured.");

        var request = new CourierRatesRequest
        {
            CollectionAddress = StoreAddress(),
            DeliveryAddress = delivery,
            Parcels = parcels,
            DeclaredValue = declaredValue
        };

        var result = await PostAsync<CourierRatesRequest, CourierRatesResponse>("rates", request);
        return result.Success
            ? CourierResult<List<CourierRate>>.Ok(result.Data?.Rates ?? new List<CourierRate>())
            : CourierResult<List<CourierRate>>.Fail(result.ErrorMessage!);
    }

    // ===================== Create shipments =====================

    public async Task<CourierResult<CourierShipment>> CreateOrderShipmentAsync(Order order, string? serviceLevelCode = null)
    {
        if (!IsConfigured) return CourierResult<CourierShipment>.Fail("Courier API is not configured.");

        if (string.IsNullOrWhiteSpace(order.DeliveryAddressLine1))
            return CourierResult<CourierShipment>.Fail("This order has no delivery address (in-store sales aren't shipped).");

        var existing = await _context.CourierShipments.FirstOrDefaultAsync(s => s.OrderId == order.OrderId && s.Status != "cancelled");
        if (existing is not null)
            return CourierResult<CourierShipment>.Fail($"Order {order.OrderNumber} already has waybill {existing.TrackingReference}.");

        var request = new CourierCreateShipmentRequest
        {
            CollectionAddress = StoreAddress(),
            CollectionContact = StoreContact(),
            DeliveryAddress = new CourierAddress
            {
                Type = "residential",
                StreetAddress = string.IsNullOrWhiteSpace(order.DeliveryAddressLine2)
                    ? order.DeliveryAddressLine1
                    : $"{order.DeliveryAddressLine1}, {order.DeliveryAddressLine2}",
                City = order.DeliveryCity,
                Zone = order.DeliveryProvince,
                Code = order.DeliveryPostalCode,
                Country = "ZA"
            },
            DeliveryContact = new CourierContact
            {
                Name = order.DeliveryRecipientName,
                MobileNumber = order.DeliveryPhoneNumber,
                Email = order.Customer?.Email
            },
            Parcels = new List<CourierParcel> { DefaultParcel($"Order {order.OrderNumber}") },
            ServiceLevelCode = serviceLevelCode ?? _options.DefaultServiceLevelCode,
            DeclaredValue = order.GrandTotal,
            CustomerReference = order.OrderNumber,
            CustomerReferenceName = "Order no.",
            MuteNotifications = false
        };

        return await CreateShipmentAsync(request, order.OrderId, null);
    }

    public async Task<CourierResult<CourierShipment>> CreatePurchaseOrderShipmentAsync(PurchaseOrder purchaseOrder, string? serviceLevelCode = null)
    {
        if (!IsConfigured) return CourierResult<CourierShipment>.Fail("Courier API is not configured.");

        var supplier = purchaseOrder.Supplier;
        if (supplier is null || string.IsNullOrWhiteSpace(supplier.StreetAddress))
            return CourierResult<CourierShipment>.Fail($"Supplier has no collection address on file - add one before booking a collection.");

        var existing = await _context.CourierShipments.FirstOrDefaultAsync(s => s.PurchaseOrderId == purchaseOrder.PurchaseOrderId && s.Status != "cancelled");
        if (existing is not null)
            return CourierResult<CourierShipment>.Fail($"{purchaseOrder.PONumber} already has waybill {existing.TrackingReference}.");

        // Inbound stock: collection is the SUPPLIER, delivery is US - the reverse of a customer order.
        var request = new CourierCreateShipmentRequest
        {
            CollectionAddress = new CourierAddress
            {
                Type = "business",
                Company = supplier.Name,
                StreetAddress = supplier.StreetAddress,
                LocalArea = supplier.LocalArea,
                City = supplier.City,
                Zone = supplier.Province,
                Code = supplier.PostalCode,
                Country = "ZA"
            },
            CollectionContact = new CourierContact
            {
                Name = supplier.ContactName ?? supplier.Name,
                MobileNumber = supplier.ContactPhone,
                Email = supplier.ContactEmail
            },
            DeliveryAddress = StoreAddress(),
            DeliveryContact = StoreContact(),
            Parcels = new List<CourierParcel> { DefaultParcel($"Restock {purchaseOrder.PONumber}") },
            ServiceLevelCode = serviceLevelCode ?? _options.DefaultServiceLevelCode,
            DeclaredValue = purchaseOrder.TotalCost,
            CustomerReference = purchaseOrder.PONumber,
            CustomerReferenceName = "PO number",
            MuteNotifications = false
        };

        return await CreateShipmentAsync(request, null, purchaseOrder.PurchaseOrderId);
    }

    private async Task<CourierResult<CourierShipment>> CreateShipmentAsync(CourierCreateShipmentRequest request, int? orderId, int? purchaseOrderId)
    {
        var result = await PostAsync<CourierCreateShipmentRequest, CourierShipmentResponse>("shipments", request);
        if (!result.Success) return CourierResult<CourierShipment>.Fail(result.ErrorMessage!);

        var payload = result.Data!;
        var shipment = new CourierShipment
        {
            OrderId = orderId,
            PurchaseOrderId = purchaseOrderId,
            ProviderShipmentId = payload.Id ?? 0,
            TrackingReference = payload.ShortTrackingReference ?? payload.CustomTrackingReference ?? string.Empty,
            CustomerReference = payload.CustomerReference ?? request.CustomerReference,
            Status = payload.Status ?? "submitted",
            ServiceLevelCode = payload.ServiceLevelCode ?? request.ServiceLevelCode,
            ServiceLevelName = payload.ServiceLevelName,
            Rate = payload.Rate,
            EstimatedCollection = payload.EstimatedCollection,
            EstimatedDeliveryFrom = payload.EstimatedDeliveryFrom,
            EstimatedDeliveryTo = payload.EstimatedDeliveryTo,
            LastSyncedAt = DateTime.UtcNow
        };

        _context.CourierShipments.Add(shipment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created courier shipment {Ref} (provider id {Id}).", shipment.TrackingReference, shipment.ProviderShipmentId);
        return CourierResult<CourierShipment>.Ok(shipment);
    }

    // ===================== Tracking =====================

    public async Task<CourierResult<CourierShipment>> RefreshTrackingAsync(int courierShipmentId, bool force = false)
    {
        var shipment = await _context.CourierShipments
            .Include(s => s.TrackingEvents)
            .FirstOrDefaultAsync(s => s.CourierShipmentId == courierShipmentId);

        if (shipment is null) return CourierResult<CourierShipment>.Fail("Shipment not found.");
        if (!IsConfigured) return CourierResult<CourierShipment>.Fail("Courier API is not configured.");

        // Don't re-poll a finished shipment, and respect the cache window otherwise - their API
        // rate-limits (429) and a delivered parcel will never change again.
        if (!force && (shipment.IsComplete ||
            (shipment.LastSyncedAt.HasValue && shipment.LastSyncedAt.Value.AddMinutes(_options.TrackingCacheMinutes) > DateTime.UtcNow)))
        {
            return CourierResult<CourierShipment>.Ok(shipment);
        }

        var result = await GetAsync<CourierTrackingResponse>($"tracking/shipments?tracking_reference={Uri.EscapeDataString(shipment.TrackingReference)}");
        if (!result.Success) return CourierResult<CourierShipment>.Fail(result.ErrorMessage!);

        var tracking = result.Data!;
        if (!string.IsNullOrWhiteSpace(tracking.Status)) shipment.Status = tracking.Status;
        shipment.CollectedDate ??= tracking.ShipmentCollectedDate;
        shipment.DeliveredDate ??= tracking.ShipmentDeliveredDate;
        shipment.EstimatedDeliveryFrom = tracking.EstimatedDeliveryFrom ?? shipment.EstimatedDeliveryFrom;
        shipment.EstimatedDeliveryTo = tracking.EstimatedDeliveryTo ?? shipment.EstimatedDeliveryTo;
        shipment.LastSyncedAt = DateTime.UtcNow;

        // Append only events we haven't stored yet - de-duped on the courier's own event id, so
        // repeated polls don't pile up duplicates. parcel_id > 0 events are per-parcel noise for
        // a single-parcel shipment, so only shipment-level events (parcel_id == 0) are kept.
        var knownEventIds = shipment.TrackingEvents.Select(e => e.ProviderEventId).ToHashSet();
        foreach (var ev in (tracking.TrackingEvents ?? new()).Where(e => e.ParcelId == 0 && !knownEventIds.Contains(e.Id)))
        {
            shipment.TrackingEvents.Add(new CourierTrackingEvent
            {
                ProviderEventId = ev.Id,
                Status = ev.Status ?? string.Empty,
                Message = ev.Message,
                Location = ev.Location,
                EventDate = ev.Date
            });
        }

        await _context.SaveChangesAsync();
        return CourierResult<CourierShipment>.Ok(shipment);
    }

    public async Task<CourierResult<bool>> CancelShipmentAsync(int courierShipmentId)
    {
        var shipment = await _context.CourierShipments.FindAsync(courierShipmentId);
        if (shipment is null) return CourierResult<bool>.Fail("Shipment not found.");
        if (!IsConfigured) return CourierResult<bool>.Fail("Courier API is not configured.");

        var result = await PostAsync<CourierCancelRequest, object>("shipments/cancel",
            new CourierCancelRequest { TrackingReference = shipment.TrackingReference });

        if (!result.Success) return CourierResult<bool>.Fail(result.ErrorMessage!);

        shipment.Status = "cancelled";
        shipment.LastSyncedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return CourierResult<bool>.Ok(true);
    }

    public async Task<CourierResult<string>> GetLabelUrlAsync(int courierShipmentId)
    {
        var shipment = await _context.CourierShipments.FindAsync(courierShipmentId);
        if (shipment is null) return CourierResult<string>.Fail("Shipment not found.");
        if (!IsConfigured) return CourierResult<string>.Fail("Courier API is not configured.");

        // Their label endpoint takes the numeric shipment id and returns a signed S3 URL.
        var result = await GetRawAsync($"shipments/label?id={shipment.ProviderShipmentId}");
        return result.Success
            ? CourierResult<string>.Ok(result.Data!.Trim('"'))
            : CourierResult<string>.Fail(result.ErrorMessage!);
    }

    // ===================== HTTP plumbing =====================

    private async Task<CourierResult<TResponse>> PostAsync<TRequest, TResponse>(string path, TRequest body)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(path, body, JsonOpts);
            var raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return CourierResult<TResponse>.Fail(DescribeError(response.StatusCode, raw));

            var data = JsonSerializer.Deserialize<TResponse>(raw, JsonOpts);
            return data is null
                ? CourierResult<TResponse>.Fail("Courier returned an empty response.")
                : CourierResult<TResponse>.Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Courier POST {Path} failed.", path);
            return CourierResult<TResponse>.Fail("Could not reach the courier service. Try again shortly.");
        }
    }

    private async Task<CourierResult<T>> GetAsync<T>(string path)
    {
        var raw = await GetRawAsync(path);
        if (!raw.Success) return CourierResult<T>.Fail(raw.ErrorMessage!);

        try
        {
            var data = JsonSerializer.Deserialize<T>(raw.Data!, JsonOpts);
            return data is null
                ? CourierResult<T>.Fail("Courier returned an empty response.")
                : CourierResult<T>.Ok(data);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not parse courier response from {Path}.", path);
            return CourierResult<T>.Fail("Courier returned an unexpected response format.");
        }
    }

    private async Task<CourierResult<string>> GetRawAsync(string path)
    {
        try
        {
            var response = await _http.GetAsync(path);
            var raw = await response.Content.ReadAsStringAsync();

            return response.IsSuccessStatusCode
                ? CourierResult<string>.Ok(raw)
                : CourierResult<string>.Fail(DescribeError(response.StatusCode, raw));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Courier GET {Path} failed.", path);
            return CourierResult<string>.Fail("Could not reach the courier service. Try again shortly.");
        }
    }

    /// <summary>Turns a courier HTTP failure into something a staff member can act on. Their
    /// documented codes carry specific meanings worth surfacing rather than a generic error.</summary>
    private string DescribeError(System.Net.HttpStatusCode status, string raw)
    {
        var detail = string.IsNullOrWhiteSpace(raw) ? "" : $" ({raw.Trim()})";
        _logger.LogWarning("Courier API returned {Status}: {Body}", (int)status, raw);

        return (int)status switch
        {
            400 => $"The courier rejected the request{detail}",
            401 => "Courier authentication failed - check the API key.",
            403 => $"The courier account isn't permitted to do that{detail}",
            423 => "The courier account is closed.",
            429 => "Too many courier requests right now - try again in a minute.",
            >= 500 => "The courier service is having problems. Try again shortly.",
            _ => $"Courier request failed with status {(int)status}{detail}"
        };
    }

    private CourierAddress StoreAddress() => new()
    {
        Type = "business",
        Company = _options.StoreAddress.Company,
        StreetAddress = _options.StoreAddress.StreetAddress,
        LocalArea = _options.StoreAddress.LocalArea,
        City = _options.StoreAddress.City,
        Zone = _options.StoreAddress.Zone,
        Country = _options.StoreAddress.Country,
        Code = _options.StoreAddress.Code,
        Lat = _options.StoreAddress.Lat,
        Lng = _options.StoreAddress.Lng
    };

    private CourierContact StoreContact() => new()
    {
        Name = _options.StoreContact.Name,
        MobileNumber = _options.StoreContact.MobileNumber,
        Email = _options.StoreContact.Email
    };

    private CourierParcel DefaultParcel(string description) => new()
    {
        ParcelDescription = description,
        SubmittedLengthCm = _options.DefaultParcelLengthCm,
        SubmittedWidthCm = _options.DefaultParcelWidthCm,
        SubmittedHeightCm = _options.DefaultParcelHeightCm,
        SubmittedWeightKg = _options.DefaultParcelWeightKg
    };
}
