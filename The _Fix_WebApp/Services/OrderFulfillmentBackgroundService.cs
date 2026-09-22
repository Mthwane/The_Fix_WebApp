using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Services.Courier;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

/// <summary>
/// Runs on a schedule, independent of any page being open, and is the actual engine behind
/// "fulfillment is automatic - staff can only cancel and monitor" (see the order-flow planning
/// discussion this implements). Each cycle does four things:
///
///   1. Books a courier collection/delivery for anything newly ready (Processing orders,
///      Approved purchase orders) that doesn't have one yet.
///   2. Refreshes tracking for every shipment not yet in a terminal stage.
///   3. On a genuine stage change, advances Order.Status where the courier's own report is
///      authoritative enough to do so, and emails the customer their progress.
///   4. Logs everything to AuditLog (surfaced on the Dashboard's Staff Activity feed and, on
///      failure, its Attention panel) so a courier outage is visible to staff rather than an
///      order silently stalling forever.
///
/// Deliberately does NOT auto-advance a PurchaseOrder past Shipped on "delivered" - someone
/// still has to physically count the boxes and Receive them (see PurchaseOrdersController).
/// The courier's delivery report there is informational, not a sign-off.
/// </summary>
public class OrderFulfillmentBackgroundService : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromMinutes(20);

    // Raw courier statuses that map to a terminal Stage (Delivered/Closed) - see
    // CourierShipment.Stage. Excluded from every refresh cycle so a finished shipment isn't
    // pointlessly re-polled forever.
    private static readonly string[] TerminalRawStatuses =
    {
        "delivered", "collected-from-locker", "collected-from-counter",
        "cancelled", "returned-to-sender", "undeliverable"
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderFulfillmentBackgroundService> _logger;

    public OrderFulfillmentBackgroundService(IServiceScopeFactory scopeFactory, ILogger<OrderFulfillmentBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial delay so this doesn't compete with the app's own startup work.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A cycle failing must never crash the app or stop future cycles - same
                // "degrade, never break" principle ICourierService itself follows.
                _logger.LogError(ex, "Order fulfillment background cycle failed unexpectedly.");
            }

            try { await Task.Delay(CycleInterval, stoppingToken); } catch (TaskCanceledException) { }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var courier = scope.ServiceProvider.GetRequiredService<Courier.ICourierService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        if (!courier.IsConfigured)
        {
            // Debug, not Warning - this is the expected state until a real API key is
            // configured (see CourierGuyOptions.IsConfigured), not a fault.
            _logger.LogDebug("Courier service has no API key configured - skipping this fulfillment cycle.");
            return;
        }

        await AutoBookOrdersAsync(context, courier, ct);
        await AutoBookPurchaseOrdersAsync(context, courier, ct);
        await RefreshOrderShipmentsAsync(context, courier, emailSender, ct);
        await RefreshPurchaseOrderShipmentsAsync(context, courier, ct);
    }

    private async Task AutoBookOrdersAsync(ApplicationDbContext context, Courier.ICourierService courier, CancellationToken ct)
    {
        var toBook = await context.Orders
            .Where(o => o.Status == OrderStatus.Processing)
            .Where(o => !context.CourierShipments.Any(s => s.OrderId == o.OrderId))
            .Include(o => o.Customer)
            .ToListAsync(ct);

        foreach (var order in toBook)
        {
            var result = await courier.CreateOrderShipmentAsync(order);
            if (result.Success)
            {
                order.Status = OrderStatus.Shipped;
                context.AuditLogs.Add(new AuditLog
                {
                    Action = "AutoBookedDelivery",
                    Details = $"Order {order.OrderNumber} - waybill {result.Data!.TrackingReference} booked automatically."
                });
            }
            else
            {
                _logger.LogWarning("Auto-booking failed for order {OrderNumber}: {Error}", order.OrderNumber, result.ErrorMessage);
                context.AuditLogs.Add(new AuditLog
                {
                    Action = "AutoBookingFailed",
                    Details = $"Order {order.OrderNumber}: {result.ErrorMessage}"
                });
            }
        }

        if (toBook.Count > 0) await context.SaveChangesAsync(ct);
    }

    private async Task AutoBookPurchaseOrdersAsync(ApplicationDbContext context, Courier.ICourierService courier, CancellationToken ct)
    {
        var toBook = await context.PurchaseOrders
            .Where(p => p.Status == PurchaseOrderStatus.Approved)
            .Where(p => !context.CourierShipments.Any(s => s.PurchaseOrderId == p.PurchaseOrderId))
            .Include(p => p.Supplier)
            .ToListAsync(ct);

        foreach (var po in toBook)
        {
            var result = await courier.CreatePurchaseOrderShipmentAsync(po);
            if (result.Success)
            {
                po.Status = PurchaseOrderStatus.Shipped;
                context.AuditLogs.Add(new AuditLog
                {
                    Action = "AutoBookedCollection",
                    Details = $"PO {po.PONumber} - waybill {result.Data!.TrackingReference} booked automatically from {po.Supplier.Name}."
                });
            }
            else
            {
                _logger.LogWarning("Auto-booking failed for PO {PONumber}: {Error}", po.PONumber, result.ErrorMessage);
                context.AuditLogs.Add(new AuditLog
                {
                    Action = "AutoBookingFailed",
                    Details = $"PO {po.PONumber}: {result.ErrorMessage}"
                });
            }
        }

        if (toBook.Count > 0) await context.SaveChangesAsync(ct);
    }

    private async Task RefreshOrderShipmentsAsync(ApplicationDbContext context, Courier.ICourierService courier, IEmailSender emailSender, CancellationToken ct)
    {
        var shipments = await context.CourierShipments
            .Include(s => s.Order).ThenInclude(o => o!.Customer)
            .Where(s => s.OrderId != null && !TerminalRawStatuses.Contains(s.Status))
            .ToListAsync(ct);

        var anyChanged = false;

        foreach (var shipment in shipments)
        {
            var previousStage = shipment.Stage;
            var result = await courier.RefreshTrackingAsync(shipment.CourierShipmentId);
            if (!result.Success) continue;

            var updated = result.Data!;
            if (updated.Stage == previousStage) continue; // nothing changed - no email, no log noise

            anyChanged = true;
            var order = updated.Order;
            if (order is null) continue;

            // The courier's own delivery report IS authoritative for a customer order (unlike
            // a purchase order, nobody physically re-counts a customer's parcel) - so this is
            // the one place Status legitimately advances without a staff action.
            if (updated.Stage == "Delivered" && order.Status != OrderStatus.Delivered)
            {
                order.Status = OrderStatus.Delivered;
                order.DateFulfilled = DateTime.UtcNow;
            }

            context.AuditLogs.Add(new AuditLog
            {
                Action = "ShipmentStageChanged",
                Details = $"Order {order.OrderNumber}: {previousStage} -> {updated.Stage}."
            });

            if (order.Customer is not null && !string.IsNullOrWhiteSpace(order.Customer.Email))
            {
                await emailSender.SendAsync(
                    order.Customer.Email,
                    $"Order {order.OrderNumber} update: {updated.Stage}",
                    BuildProgressEmailBody(order, updated));
            }
        }

        if (anyChanged) await context.SaveChangesAsync(ct);
    }

    private async Task RefreshPurchaseOrderShipmentsAsync(ApplicationDbContext context, Courier.ICourierService courier, CancellationToken ct)
    {
        var shipments = await context.CourierShipments
            .Include(s => s.PurchaseOrder)
            .Where(s => s.PurchaseOrderId != null && !TerminalRawStatuses.Contains(s.Status))
            .ToListAsync(ct);

        var anyChanged = false;

        foreach (var shipment in shipments)
        {
            var previousStage = shipment.Stage;
            var result = await courier.RefreshTrackingAsync(shipment.CourierShipmentId);
            if (!result.Success) continue;

            var updated = result.Data!;
            if (updated.Stage == previousStage) continue;

            anyChanged = true;
            var po = updated.PurchaseOrder;
            if (po is null) continue;

            // Status is deliberately NOT advanced here - Received only ever happens through
            // PurchaseOrdersController.Receive, where a human confirms actual line quantities.
            // This is purely "here's what the courier says, go check it."
            context.AuditLogs.Add(new AuditLog
            {
                Action = "ShipmentStageChanged",
                Details = $"PO {po.PONumber}: {previousStage} -> {updated.Stage}."
                    + (updated.Stage == "Delivered" ? " Ready to receive." : "")
            });
        }

        if (anyChanged) await context.SaveChangesAsync(ct);
    }

    private static string BuildProgressEmailBody(Order order, CourierShipment shipment)
    {
        var message = shipment.Stage switch
        {
            "Collected" => "has been collected and is on its way to you",
            "In Transit" => "is in transit",
            "Delivered" => "has been delivered",
            _ => $"is now: {shipment.Stage}"
        };

        return $"<p>Hi {order.Customer?.FullName},</p>" +
               $"<p>Your order <strong>{order.OrderNumber}</strong> {message}.</p>" +
               $"<p>Tracking reference: <strong>{shipment.TrackingReference}</strong></p>";
    }
}
