using System.Net;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services;

public class OrderFulfillmentService : IOrderFulfillmentService
{
    private const int MaxAttempts = 3;

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly IRewardsService _rewards;
    private readonly IDiscountService _discounts;
    private readonly ILogger<OrderFulfillmentService> _logger;

    public OrderFulfillmentService(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        IRewardsService rewards,
        IDiscountService discounts,
        ILogger<OrderFulfillmentService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _rewards = rewards;
        _discounts = discounts;
        _logger = logger;
    }

    public async Task<Order> CreateOnlineOrderAsync(
        ApplicationUser customer,
        CartViewModel cart,
        PaymentMethod paymentMethod,
        string reference,
        CustomerAddress? deliveryAddress,
        decimal pointsDiscount = 0m,
        decimal walletAmount = 0m,
        string? discountCode = null,
        decimal discountAmount = 0m)
    {
        discountCode = string.IsNullOrWhiteSpace(discountCode) ? null : _discounts.NormalizeCode(discountCode);
        var codeSaving = discountCode is null ? 0m : Math.Max(discountAmount, 0m);
        var pointsSaving = Math.Max(pointsDiscount, 0m);

        // The two savings stack, but together can never exceed the basket.
        var discount = Math.Min(codeSaving + pointsSaving, cart.SubTotal);
        var vat = TaxSettings.CalculateVat(cart.SubTotal, discount);
        var deliveryFee = DeliverySettings.CalculateFee(cart.SubTotal); // flat R100 under R500, free from R500
        var grand = cart.SubTotal - discount + vat + deliveryFee;
        var walletPart = Math.Round(Math.Min(Math.Max(walletAmount, 0m), grand), 2, MidpointRounding.AwayFromZero);

        var (order, createdNow) = await PersistWithRetryAsync(customer, cart, paymentMethod, reference, deliveryAddress, discount, vat, grand, walletPart, discountCode, codeSaving);

        // Everything below runs AFTER the order is safely committed and must never undo it - the money
        // has already moved, so a failure here is logged for follow-up, not thrown.
        var pointsEarned = 0;
        try
        {
            if (pointsSaving > 0)
                await _rewards.LinkOrderAsync(reference, order.OrderId);

            pointsEarned = (await _rewards.EarnForOrderAsync(order)).Points;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order {OrderNumber} was placed but awarding/linking reward points failed.", order.OrderNumber);
        }

        if (createdNow)
            await SendConfirmationAsync(customer, cart, order, deliveryAddress, pointsEarned);

        return order;
    }

    // ---------- Persistence ----------

    private async Task<(Order Order, bool CreatedNow)> PersistWithRetryAsync(
        ApplicationUser customer, CartViewModel cart, PaymentMethod paymentMethod, string reference,
        CustomerAddress? deliveryAddress, decimal discount, decimal vat, decimal grand, decimal walletPart,
        string? discountCode, decimal codeSaving)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Idempotent per reference: an earlier attempt may have committed even though the caller saw an error
            // (connection dropped on the commit, a double-click on the Paystack callback).
            var existing = await _context.Orders.FirstOrDefaultAsync(o => o.OrderNumber == reference);
            if (existing is not null)
            {
                _logger.LogInformation("Order {OrderNumber} already exists - returning it instead of creating a duplicate.", reference);
                return (existing, false);
            }

            try
            {
                var order = await TryCreateOnceAsync(customer, cart, paymentMethod, reference, deliveryAddress, discount, vat, grand, walletPart, discountCode, codeSaving);
                return (order, true);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                _logger.LogWarning(ex, "Creating order {OrderNumber} failed on attempt {Attempt} - retrying.", reference, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }
    }

    private async Task<Order> TryCreateOnceAsync(
        ApplicationUser customer, CartViewModel cart, PaymentMethod paymentMethod, string reference,
        CustomerAddress? deliveryAddress, decimal discount, decimal vat, decimal grand, decimal walletPart,
        string? discountCode, decimal codeSaving)
    {
        var order = new Order
        {
            OrderNumber = reference,
            OrderType = OrderType.Online,
            Status = OrderStatus.Processing,
            PaymentMethod = paymentMethod,
            CustomerId = customer.Id,
            SubTotal = cart.SubTotal,
            DiscountTotal = discount,
            DiscountCode = discountCode,
            TaxTotal = vat,
            DeliveryFee = DeliverySettings.CalculateFee(cart.SubTotal),
            GrandTotal = grand,
            WalletAmountApplied = walletPart,

            // Snapshot the address at the moment of purchase - if the customer edits or deletes this
            // saved address later, this order still shows where it actually went.
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

        // The order and its stock movement commit together or not at all.
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Spend one redemption of the code in this same transaction - atomic and limit-checked, so the last
            // use of a limited code can't be taken twice. Throws DiscountUnavailableException (not retried) if it ran out.
            if (discountCode is not null)
                await _discounts.RedeemAsync(discountCode, order.OrderId, customer.Id, codeSaving);

            // Atomic, conditional UPDATE per variant (see InventoryService) - joins this transaction.
            var updatedVariants = await _inventoryService.DecrementStockBatchAsync(
                cart.Lines.Select(l => (l.VariantId, l.Quantity)));

            var lowStockVariantIds = updatedVariants.Where(v => v.IsLowStock).Select(v => v.ProductVariantId).ToHashSet();
            var newlyLowStock = cart.Lines.Where(l => lowStockVariantIds.Contains(l.VariantId)).Select(l => l.Name).ToList();
            if (newlyLowStock.Count > 0)
                _logger.LogInformation("Online order pushed these products into low stock: {Products}", string.Join(", ", newlyLowStock));

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = customer.Id,
                Action = "OnlineOrderPlaced",
                Details = $"Placed order {order.OrderNumber} for {order.GrandTotal:C} ({cart.Lines.Count} line item(s))." +
                          (discountCode is not null ? $" Discount code {discountCode} -{codeSaving:C}." : "") +
                          (discount - codeSaving > 0 ? $" Points discount {(discount - codeSaving):C}." : "") +
                          (walletPart > 0 ? $" FixCash {walletPart:C}." : "")
            });
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
            return order;
        }
        catch
        {
            await tx.RollbackAsync();
            DetachRolledBackEntities();
            throw;
        }
    }

    /// <summary>After a rollback the change tracker still holds the rows that never made it to the database.</summary>
    private void DetachRolledBackEntities()
    {
        foreach (var entry in _context.ChangeTracker.Entries()
                     .Where(e => e.Entity is Order or OrderItem or InventoryTransaction or AuditLog or DiscountRedemption)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>A database fault worth retrying. A genuine stock shortage is not - trying again can't create stock.</summary>
    private static bool IsTransient(Exception ex) =>
        ex is not InsufficientStockException
        && (ex is DbUpdateException or SqlException or TimeoutException);

    // ---------- Confirmation email ----------

    private async Task SendConfirmationAsync(ApplicationUser customer, CartViewModel cart, Order order, CustomerAddress? deliveryAddress, int pointsEarned)
    {
        if (string.IsNullOrWhiteSpace(customer.Email)) return;

        try
        {
            static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

            var itemsHtml = string.Join("", cart.Lines.Select(l =>
                $"<tr><td>{E(l.Name)}</td><td>{l.Quantity}</td><td>{l.UnitPrice:C}</td><td>{l.LineTotal:C}</td></tr>"));

            var addressHtml = deliveryAddress is null
                ? ""
                : $@"<p>Delivering to:<br/>{E(order.DeliveryRecipientName)}<br/>{E(order.DeliveryAddressLine1)}
                    {(string.IsNullOrWhiteSpace(order.DeliveryAddressLine2) ? "" : "<br/>" + E(order.DeliveryAddressLine2))}<br/>
                    {E(order.DeliveryCity)}, {E(order.DeliveryProvince)} {E(order.DeliveryPostalCode)}</p>";

            var discountLine = order.DiscountTotal > 0 ? $"Discount{(order.DiscountCode is null ? "" : $" ({E(order.DiscountCode)})")}: -{order.DiscountTotal:C}<br/>" : "";
            var splitLine = order.WalletAmountApplied > 0 && order.WalletAmountApplied < order.GrandTotal
                ? $"Paid from FixCash: {order.WalletAmountApplied:C}<br/>Paid by card: {(order.GrandTotal - order.WalletAmountApplied):C}<br/>"
                : "";
            var pointsLine = pointsEarned > 0 ? $"<p>You earned <strong>{pointsEarned}</strong> FixRewards points on this order.</p>" : "";

            var body = $@"
                <h2>Thanks for your order, {E(customer.FullName)}!</h2>
                <p>Order <strong>{E(order.OrderNumber)}</strong> has been received and is being processed.</p>
                <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                    <thead><tr><th>Item</th><th>Qty</th><th>Unit Price</th><th>Line Total</th></tr></thead>
                    <tbody>{itemsHtml}</tbody>
                </table>
                <p>Subtotal: {order.SubTotal:C}<br/>{discountLine}VAT (15%): {order.TaxTotal:C}<br/>
                <strong>Total: {order.GrandTotal:C}</strong><br/>{splitLine}</p>
                {pointsLine}
                {addressHtml}
                <p>You can track this order any time under My Orders.</p>";

            await _emailSender.SendAsync(customer.Email, $"Order Confirmation - {order.OrderNumber}", body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order {OrderNumber} was placed but the confirmation email failed.", order.OrderNumber);
        }
    }
}
