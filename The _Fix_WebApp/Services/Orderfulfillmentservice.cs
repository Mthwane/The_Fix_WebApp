using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

public class OrderFulfillmentService : IOrderFulfillmentService
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly IRewardsService _rewards;
    private readonly ILogger<OrderFulfillmentService> _logger;

    public OrderFulfillmentService(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        IRewardsService rewards,
        ILogger<OrderFulfillmentService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _rewards = rewards;
        _logger = logger;
    }

    public async Task<Order> CreateOnlineOrderAsync(
        ApplicationUser customer,
        CartViewModel cart,
        PaymentMethod paymentMethod,
        string reference,
        CustomerAddress? deliveryAddress,
        decimal pointsDiscount = 0m)
    {
        var discount = Math.Min(Math.Max(pointsDiscount, 0m), cart.SubTotal);
        var vat = TaxSettings.CalculateVat(cart.SubTotal, discount);

        var order = new Order
        {
            OrderNumber = reference,
            OrderType = OrderType.Online,
            Status = OrderStatus.Processing,
            PaymentMethod = paymentMethod,
            CustomerId = customer.Id,
            SubTotal = cart.SubTotal,
            DiscountTotal = discount,
            TaxTotal = vat,
            GrandTotal = cart.SubTotal - discount + vat,

            // Snapshot the address at the moment of purchase - if the customer edits or
            // deletes this saved address later, this order still shows where it actually went.
            DeliveryRecipientName = deliveryAddress?.RecipientName,
            DeliveryPhoneNumber = deliveryAddress?.PhoneNumber,
            DeliveryAddressLine1 = deliveryAddress?.AddressLine1,
            DeliveryAddressLine2 = deliveryAddress?.AddressLine2,
            DeliveryCity = deliveryAddress?.City,
            DeliveryProvince = deliveryAddress?.Province,
            DeliveryPostalCode = deliveryAddress?.PostalCode,
        };

        foreach (var line in cart.Lines)
        {
            order.OrderItems.Add(new OrderItem
            {
                ProductId = line.ProductId,
                ProductVariantId = line.VariantId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal
            });
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // One round trip and one commit for the whole cart, instead of one query + one
        // commit per line item.
        var updatedVariants = await _inventoryService.DecrementStockBatchAsync(
            cart.Lines.Select(l => (l.VariantId, l.Quantity)));

        var lowStockVariantIds = updatedVariants.Where(v => v.IsLowStock).Select(v => v.ProductVariantId).ToHashSet();
        var newlyLowStock = cart.Lines
            .Where(l => lowStockVariantIds.Contains(l.VariantId))
            .Select(l => l.Name)
            .ToList();
        if (newlyLowStock.Count > 0)
            _logger.LogInformation("Online order pushed these products into low stock: {Products}", string.Join(", ", newlyLowStock));

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = customer.Id,
            Action = "OnlineOrderPlaced",
            Details = $"Placed order {order.OrderNumber} for {order.GrandTotal:C} ({cart.Lines.Count} line item(s))." +
                      (discount > 0 ? $" Points discount {discount:C}." : "")
        });
        await _context.SaveChangesAsync();

        // Rewards run AFTER the order is committed and must never break it - the money has
        // already moved, so a rewards failure is logged for manual follow-up, not thrown.
        var pointsEarned = 0;
        try
        {
            if (discount > 0)
                await _rewards.LinkOrderAsync(reference, order.OrderId);

            pointsEarned = (await _rewards.EarnForOrderAsync(order)).Points;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order {OrderNumber} was placed but awarding/linking reward points failed.", order.OrderNumber);
        }

        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            var itemsHtml = string.Join("", cart.Lines.Select(l =>
                $"<tr><td>{l.Name}</td><td>{l.Quantity}</td><td>{l.UnitPrice:C}</td><td>{l.LineTotal:C}</td></tr>"));

            var addressHtml = deliveryAddress is null
                ? ""
                : $@"<p>Delivering to:<br/>{order.DeliveryRecipientName}<br/>{order.DeliveryAddressLine1}
                    {(string.IsNullOrWhiteSpace(order.DeliveryAddressLine2) ? "" : "<br/>" + order.DeliveryAddressLine2)}<br/>
                    {order.DeliveryCity}, {order.DeliveryProvince} {order.DeliveryPostalCode}</p>";

            var discountLine = order.DiscountTotal > 0 ? $"Points discount: -{order.DiscountTotal:C}<br/>" : "";
            var pointsLine = pointsEarned > 0 ? $"<p>You earned <strong>{pointsEarned}</strong> FixRewards points on this order.</p>" : "";

            var body = $@"
                <h2>Thanks for your order, {customer.FullName}!</h2>
                <p>Order <strong>{order.OrderNumber}</strong> has been received and is being processed.</p>
                <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                    <thead><tr><th>Item</th><th>Qty</th><th>Unit Price</th><th>Line Total</th></tr></thead>
                    <tbody>{itemsHtml}</tbody>
                </table>
                <p>Subtotal: {order.SubTotal:C}<br/>{discountLine}VAT (15%): {order.TaxTotal:C}<br/>
                <strong>Total: {order.GrandTotal:C}</strong></p>
                {pointsLine}
                {addressHtml}
                <p>You can track this order any time under My Orders.</p>";

            await _emailSender.SendAsync(customer.Email, $"Order Confirmation - {order.OrderNumber}", body);
        }

        return order;
    }
}