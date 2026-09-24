using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services.Courier;

/// <summary>
/// A drop-in stand-in for CourierGuyService that never touches the network and never costs
/// anything - the real sandbox at shiplogic.com is a billed account (it has its own balance
/// that runs out), so repeatedly running the fulfillment cycle while iterating on this feature
/// burns real sandbox credit for no benefit. This simulates the exact same shape of behaviour
/// entirely locally: booking is instant, and each RefreshTrackingAsync call advances the
/// shipment one step through a fixed status sequence (rather than needing real courier activity
/// to progress), so a few clicks of "Run Fulfillment Cycle Now" walks a shipment all the way
/// from booked to delivered - enough to exercise auto-booking, stage-change detection, the
/// customer email, and the Track page, for free, offline, instantly.
///
/// Selected instead of CourierGuyService via the CourierGuy:Provider config switch in
/// Program.cs - see the comment there. Swap back to "Live" only for a final, deliberately
/// limited pass against the real sandbox before going live.
/// </summary>
public class FakeCourierService : ICourierService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FakeCourierService> _logger;

    // The path a shipment walks through, one step per RefreshTrackingAsync call - mirrors The
    // Courier Guy's real status vocabulary (see CourierShipment.Stage) so Stage transitions,
    // the customer email trigger, and the Track page timeline all behave exactly as they would
    // against the real service.
    private static readonly string[] StatusSequence =
    {
        "submitted", "collection-assigned", "collected", "in-transit", "out-for-delivery", "delivered"
    };

    private static readonly Dictionary<string, (string Message, string Location)> StepNarration = new()
    {
        ["submitted"] = ("Shipment booked and waiting for collection.", "Fashion Fix warehouse"),
        ["collection-assigned"] = ("A driver has been assigned to collect this parcel.", "Fashion Fix warehouse"),
        ["collected"] = ("Parcel collected from sender.", "Fashion Fix warehouse"),
        ["in-transit"] = ("Parcel in transit to the local depot.", "Regional sorting facility"),
        ["out-for-delivery"] = ("Out for delivery.", "Local depot"),
        ["delivered"] = ("Delivered.", "Delivery address"),
    };

    public FakeCourierService(ApplicationDbContext context, ILogger<FakeCourierService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Always "configured" - there's no API key to be missing, and the whole point is that the
    // fulfillment cycle runs normally without one.
    public bool IsConfigured => true;

    public Task<CourierResult<List<CourierRate>>> GetRatesAsync(CourierAddress delivery, List<CourierParcel> parcels, decimal? declaredValue = null)
    {
        var rate = new CourierRate
        {
            Rate = 65m,
            BaseRate = 65m,
            ServiceLevel = new CourierServiceLevel { Id = 1, Code = "ECO", Name = "Economy (Simulated)" }
        };
        return Task.FromResult(CourierResult<List<CourierRate>>.Ok(new List<CourierRate> { rate }));
    }

    public async Task<CourierResult<CourierShipment>> CreateOrderShipmentAsync(Order order, string? serviceLevelCode = null)
    {
        if (string.IsNullOrWhiteSpace(order.DeliveryAddressLine1))
            return CourierResult<CourierShipment>.Fail("This order has no delivery address (in-store sales aren't shipped).");

        var existing = await _context.CourierShipments.FirstOrDefaultAsync(s => s.OrderId == order.OrderId && s.Status != "cancelled");
        if (existing is not null)
            return CourierResult<CourierShipment>.Fail($"Order {order.OrderNumber} already has waybill {existing.TrackingReference}.");

        return await CreateShipmentAsync(order.OrderId, null, order.OrderNumber, serviceLevelCode);
    }

    public async Task<CourierResult<CourierShipment>> CreatePurchaseOrderShipmentAsync(PurchaseOrder purchaseOrder, string? serviceLevelCode = null)
    {
        var supplier = purchaseOrder.Supplier;
        if (supplier is null || string.IsNullOrWhiteSpace(supplier.StreetAddress))
            return CourierResult<CourierShipment>.Fail("Supplier has no collection address on file - add one before booking a collection.");

        var existing = await _context.CourierShipments.FirstOrDefaultAsync(s => s.PurchaseOrderId == purchaseOrder.PurchaseOrderId && s.Status != "cancelled");
        if (existing is not null)
            return CourierResult<CourierShipment>.Fail($"{purchaseOrder.PONumber} already has waybill {existing.TrackingReference}.");

        return await CreateShipmentAsync(null, purchaseOrder.PurchaseOrderId, purchaseOrder.PONumber, serviceLevelCode);
    }

    private async Task<CourierResult<CourierShipment>> CreateShipmentAsync(int? orderId, int? purchaseOrderId, string customerReference, string? serviceLevelCode)
    {
        var now = DateTime.UtcNow;
        var shipment = new CourierShipment
        {
            OrderId = orderId,
            PurchaseOrderId = purchaseOrderId,
            ProviderShipmentId = Random.Shared.NextInt64(100000, 999999),
            TrackingReference = "FAKE" + Random.Shared.Next(100000, 999999),
            CustomerReference = customerReference,
            Status = "submitted",
            ServiceLevelCode = serviceLevelCode ?? "ECO",
            ServiceLevelName = "Economy (Simulated)",
            Rate = 65m,
            EstimatedCollection = now.AddDays(1),
            EstimatedDeliveryFrom = now.AddDays(2),
            EstimatedDeliveryTo = now.AddDays(4),
            LastSyncedAt = now
        };
        shipment.TrackingEvents.Add(new CourierTrackingEvent
        {
            ProviderEventId = Random.Shared.NextInt64(1, int.MaxValue),
            Status = "submitted",
            Message = StepNarration["submitted"].Message,
            Location = StepNarration["submitted"].Location,
            EventDate = now
        });

        _context.CourierShipments.Add(shipment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("[FakeCourier] Created simulated shipment {Ref} for {Ref2}.", shipment.TrackingReference, customerReference);
        return CourierResult<CourierShipment>.Ok(shipment);
    }

    public async Task<CourierResult<CourierShipment>> RefreshTrackingAsync(int courierShipmentId, bool force = false)
    {
        var shipment = await _context.CourierShipments
            .Include(s => s.TrackingEvents)
            .FirstOrDefaultAsync(s => s.CourierShipmentId == courierShipmentId);

        if (shipment is null) return CourierResult<CourierShipment>.Fail("Shipment not found.");
        if (shipment.IsComplete) return CourierResult<CourierShipment>.Ok(shipment);

        var currentIndex = Array.IndexOf(StatusSequence, shipment.Status);
        var nextIndex = Math.Min(currentIndex < 0 ? 0 : currentIndex + 1, StatusSequence.Length - 1);
        var nextStatus = StatusSequence[nextIndex];

        if (nextStatus != shipment.Status)
        {
            shipment.Status = nextStatus;
            var (message, location) = StepNarration[nextStatus];
            shipment.TrackingEvents.Add(new CourierTrackingEvent
            {
                ProviderEventId = Random.Shared.NextInt64(1, int.MaxValue),
                Status = nextStatus,
                Message = message,
                Location = location,
                EventDate = DateTime.UtcNow
            });

            if (nextStatus == "collected") shipment.CollectedDate = DateTime.UtcNow;
            if (nextStatus == "delivered") shipment.DeliveredDate = DateTime.UtcNow;
        }

        shipment.LastSyncedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("[FakeCourier] {Ref} advanced to {Status}.", shipment.TrackingReference, shipment.Status);
        return CourierResult<CourierShipment>.Ok(shipment);
    }

    public async Task<CourierResult<bool>> CancelShipmentAsync(int courierShipmentId)
    {
        var shipment = await _context.CourierShipments.FindAsync(courierShipmentId);
        if (shipment is null) return CourierResult<bool>.Fail("Shipment not found.");

        shipment.Status = "cancelled";
        shipment.LastSyncedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return CourierResult<bool>.Ok(true);
    }

    public Task<CourierResult<string>> GetLabelUrlAsync(int courierShipmentId)
        => Task.FromResult(CourierResult<string>.Fail("No waybill label in simulated/fake courier mode."));
}
