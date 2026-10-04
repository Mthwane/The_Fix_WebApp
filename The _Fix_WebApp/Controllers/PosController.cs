using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace FashionFix.Web.Controllers;

[Authorize(Policy = Permissions.PosUse)]
public class PosController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly IRewardsService _rewardsService;
    private readonly IDiscountService _discountService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PosController> _logger;

    public PosController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        IRewardsService rewardsService,
        IDiscountService discountService,
        UserManager<ApplicationUser> userManager,
        ILogger<PosController> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _rewardsService = rewardsService;
        _discountService = discountService;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Pos - the till interface for staff.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User);
        var today = DateTime.UtcNow.Date;

        var todaysSales = await _context.Orders
            .Where(o => o.ProcessedByUserId == userId && o.OrderType == OrderType.POS && o.DateCreated >= today)
            .ToListAsync();

        ViewBag.TodaysTillSales = todaysSales.Sum(o => o.GrandTotal);
        ViewBag.TodaysTillTransactionCount = todaysSales.Count;

        ViewBag.CurrentShift = await _context.ShiftSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        await LoadDiscountsForTillAsync();
        return View(new POSCheckoutViewModel());
    }

    private bool CanUseDiscounts => User.HasClaim(Permissions.ClaimType, Permissions.DiscountsView);

    /// <summary>Discounts only exist at the till for roles granted discounts.view - everyone else sees no discount UI at all.</summary>
    private async Task LoadDiscountsForTillAsync()
    {
        ViewBag.CanUseDiscounts = CanUseDiscounts;
        ViewBag.LiveDiscounts = CanUseDiscounts
            ? await _discountService.GetLiveForChannelAsync(DiscountChannel.InStore)
            : new List<Discount>();
    }

    // POST: /Pos/ApplyDiscount - AJAX preview used by the "Apply" button. The cart lines are used only to learn WHICH
    // variants and HOW MANY; every price comes from the database. Nothing is consumed here - the real, atomic redemption
    // happens inside Checkout.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyDiscount([FromForm] string? code, [FromForm] string? customerId, [FromForm] List<int> variantIds, [FromForm] List<int> quantities)
    {
        if (!CanUseDiscounts)
            return Json(new { valid = false, message = "Your role isn't allowed to apply discounts." });

        variantIds ??= new(); quantities ??= new();
        if (variantIds.Count == 0 || variantIds.Count != quantities.Count)
            return Json(new { valid = false, message = "Scan at least one item first." });

        var ids = variantIds.Distinct().ToList();
        var variants = await _context.ProductVariants.AsNoTracking()
            .Include(v => v.Product)
            .Where(v => ids.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        var lines = new List<DiscountLine>();
        for (var i = 0; i < variantIds.Count; i++)
        {
            if (!variants.TryGetValue(variantIds[i], out var v) || quantities[i] < 1) continue;
            lines.Add(new DiscountLine(v.ProductId, quantities[i], Math.Round(v.EffectivePrice, 2, MidpointRounding.AwayFromZero)));
        }

        var result = await _discountService.EvaluateAsync(code, lines, DiscountChannel.InStore,
            string.IsNullOrWhiteSpace(customerId) ? null : customerId.Trim());

        return result.IsValid
            ? Json(new { valid = true, amount = result.Amount, code = result.Discount!.Code, label = result.Discount.ValueLabel(), name = result.Discount.Name })
            : Json(new { valid = false, message = result.Error });
    }

    // POST: /Pos/AutoDiscount - AJAX preview of the best AUTOMATIC discount for the current basket. Automatic discounts are
    // store promotions, so unlike a typed code every cashier gets them (no discounts.view needed). Same rule as ApplyDiscount:
    // the browser only says which variants/quantities; prices come from the database and Checkout re-evaluates everything.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoDiscount([FromForm] string? customerId, [FromForm] List<int> variantIds, [FromForm] List<int> quantities)
    {
        variantIds ??= new(); quantities ??= new();
        if (variantIds.Count == 0 || variantIds.Count != quantities.Count)
            return Json(new { valid = false });

        var ids = variantIds.Distinct().ToList();
        var variants = await _context.ProductVariants.AsNoTracking()
            .Include(v => v.Product)
            .Where(v => ids.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        var lines = new List<DiscountLine>();
        for (var i = 0; i < variantIds.Count; i++)
        {
            if (!variants.TryGetValue(variantIds[i], out var v) || quantities[i] < 1) continue;
            lines.Add(new DiscountLine(v.ProductId, quantities[i], Math.Round(v.EffectivePrice, 2, MidpointRounding.AwayFromZero)));
        }

        var result = await _discountService.EvaluateBestAutoAsync(lines, DiscountChannel.InStore,
            string.IsNullOrWhiteSpace(customerId) ? null : customerId.Trim());

        return result.IsValid
            ? Json(new { valid = true, amount = result.Amount, code = result.Discount!.Code, label = result.Discount.ValueLabel(), name = result.Discount.Name })
            : Json(new { valid = false });
    }

    // GET: /Pos/StartShift - opening float entry. Not a hard gate on using the till (POS
    // access isn't blocked without an open shift, to avoid a risky behavioural change to an
    // already-working screen) - this is opt-in, for stores that want the cash reconciliation.
    [HttpGet]
    public async Task<IActionResult> StartShift()
    {
        var userId = _userManager.GetUserId(User);
        var existing = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (existing is not null)
        {
            this.ToastWarning("You already have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        return View();
    }

    // POST: /Pos/StartShift
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartShift(decimal openingFloat, string? returnUrl = null)
    {
        var userId = _userManager.GetUserId(User)!;
        var existing = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (existing is not null)
        {
            this.ToastWarning("You already have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        if (openingFloat < 0)
        {
            this.ToastError("Opening float can't be negative.");
            return View();
        }

        _context.ShiftSessions.Add(new ShiftSession { UserId = userId, OpeningFloat = openingFloat });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Shift started with an opening float of {openingFloat:C}.");

        // Clocking on: if stock is running low and this person can raise restock requests, take them straight to
        // Purchase Orders (the low-stock queue) so the day starts with what needs ordering.
        if (User.HasClaim(Permissions.ClaimType, Permissions.PurchaseOrdersManage))
        {
            var lowCount = await _context.ProductVariants.AsNoTracking()
                .CountAsync(v => v.IsActive && v.Product.IsActive && v.StockQuantity <= v.Product.LowStockThreshold);
            if (lowCount > 0)
            {
                this.ToastWarning($"{lowCount} item{(lowCount == 1 ? " is" : "s are")} low on stock - review and raise a restock request below.");
                return RedirectToAction("LowStock", "PurchaseOrders");
            }
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    // GET: /Pos/EndShift - shows expected cash (opening float + cash sales since open) so the
    // operator can count the drawer against it before confirming.
    [HttpGet]
    public async Task<IActionResult> EndShift()
    {
        var userId = _userManager.GetUserId(User);
        var shift = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (shift is null)
        {
            this.ToastWarning("You don't have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        var cashSales = await _context.Orders
            .Where(o => o.ProcessedByUserId == userId && o.PaymentMethod == PaymentMethod.Cash && o.DateCreated >= shift.DateOpened)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;

        ViewBag.ExpectedCash = shift.OpeningFloat + cashSales;
        return View(shift);
    }

    // POST: /Pos/EndShift
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndShift(decimal closingFloat, string? notes)
    {
        var userId = _userManager.GetUserId(User);
        var shift = await _context.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (shift is null)
        {
            this.ToastWarning("You don't have a shift open.");
            return RedirectToAction(nameof(Index));
        }

        if (closingFloat < 0)
        {
            this.ToastError("Closing float can't be negative.");
            return RedirectToAction(nameof(EndShift));
        }

        var cashSales = await _context.Orders
            .Where(o => o.ProcessedByUserId == userId && o.PaymentMethod == PaymentMethod.Cash && o.DateCreated >= shift.DateOpened)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;
        var expectedCash = shift.OpeningFloat + cashSales;
        var variance = closingFloat - expectedCash;

        shift.ClosingFloat = closingFloat;
        shift.DateClosed = DateTime.UtcNow;
        shift.Status = ShiftStatus.Closed;
        shift.Notes = notes;
        await _context.SaveChangesAsync();

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "ShiftClosed",
            Details = $"Closed shift - expected {expectedCash:C}, counted {closingFloat:C}, variance {variance:C}."
        });
        await _context.SaveChangesAsync();

        if (Math.Abs(variance) < 0.01m)
            this.ToastSuccess($"Shift closed - drawer balanced exactly ({closingFloat:C}).");
        else
            this.ToastWarning($"Shift closed - {(variance > 0 ? "over" : "short")} by {Math.Abs(variance):C} (expected {expectedCash:C}, counted {closingFloat:C}).");

        return RedirectToAction(nameof(Index));
    }

    // POST: /Pos/Checkout - scans/cart lines already built client-side (barcode JS), submitted here.
    //
    // NOTHING money-related is trusted from the browser. The cart lines are used only to learn WHICH
    // variants and HOW MANY were scanned; every price, name and SKU is re-read from the database, VAT is
    // recomputed here, and the discount is range-checked. The order row and its stock movement are
    // committed in one transaction, so a failed sale leaves neither a phantom order nor missing stock.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(POSCheckoutViewModel model)
    {
        await LoadDiscountsForTillAsync(); // so every "return View(Index)" below still shows the discount section
        model.DiscountTotal = 0m;          // never trust a posted amount - it's recomputed from the code below

        if (model.CartItems.Count == 0)
        {
            this.ToastError("The till is empty - scan at least one item before completing the sale.");
            return View(nameof(Index), model);
        }

        // Every sale must be traceable to someone: a customer account ID, or an email address for the receipt.
        if (string.IsNullOrWhiteSpace(model.CustomerId) && string.IsNullOrWhiteSpace(model.ReceiptEmail))
        {
            this.ToastError("Enter the customer's ID or an email address before completing the sale.");
            return View(nameof(Index), model);
        }

        if (!ModelState.IsValid)
        {
            var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            this.ToastError(string.IsNullOrWhiteSpace(errors)
                ? "Couldn't complete the sale - please check the details and try again."
                : $"Couldn't complete the sale: {errors}");
            return View(nameof(Index), model);
        }

        // FixCash is an online-only wallet - it can't be spent at the till, whatever the form says.
        if (model.PaymentMethod == PaymentMethod.FixCash)
        {
            this.ToastError("FixCash can only be used online, not at the till. Please choose another payment method.");
            return View(nameof(Index), model);
        }

        // One query for the whole basket, keyed to the exact variant (size/colour) scanned.
        var cartVariantIds = model.CartItems.Select(l => l.VariantId).Distinct().ToList();
        var currentVariants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => cartVariantIds.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        // --- Re-price every line from the database ---
        var priceChanges = new List<string>();
        foreach (var line in model.CartItems)
        {
            if (!currentVariants.TryGetValue(line.VariantId, out var variant) || !variant.IsActive || !variant.Product.IsActive)
            {
                this.ToastError($"'{line.ProductName}' is no longer available - please remove it from the till.");
                return View(nameof(Index), model);
            }

            var serverPrice = Math.Round(variant.EffectivePrice, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(line.UnitPrice - serverPrice) > 0.005m)
                priceChanges.Add($"{variant.Product.Name} ({variant.Size}/{variant.Color}): {line.UnitPrice:C} -> {serverPrice:C}");

            // Overwrite everything descriptive/financial with the database's version.
            line.ProductId = variant.ProductId;
            line.ProductName = variant.Product.Name;
            line.SKU = variant.SKU;
            line.Size = variant.Size;
            line.Color = variant.Color;
            line.UnitPrice = serverPrice;
        }

        if (priceChanges.Count > 0)
        {
            // The cashier and customer saw a different price on screen. Don't silently charge something else:
            // show the corrected prices and let the cashier confirm again.
            ModelState.Clear();
            this.ToastWarning($"Prices changed since these items were scanned - the till now shows the current prices. Please check and complete the sale again. ({string.Join("; ", priceChanges)})");
            return View(nameof(Index), model);
        }

        // --- Stock (per variant, summing duplicate lines) ---
        foreach (var group in model.CartItems.GroupBy(l => l.VariantId))
        {
            var variant = currentVariants[group.Key];
            var wanted = group.Sum(l => l.Quantity);
            if (variant.StockQuantity < wanted)
            {
                this.ToastError($"Only {variant.StockQuantity} of '{variant.Product.Name}' ({variant.Size}/{variant.Color}) left in stock - please adjust the quantity.");
                return View(nameof(Index), model);
            }
        }

        // --- Linked customer must be a real Customer account (it earns reward points) ---
        if (!string.IsNullOrWhiteSpace(model.CustomerId))
        {
            var linked = await _userManager.FindByIdAsync(model.CustomerId);
            if (linked is null || !await _userManager.IsInRoleAsync(linked, "Customer"))
            {
                this.ToastError("The linked customer account wasn't found. Clear the customer field or pick a valid customer.");
                return View(nameof(Index), model);
            }
        }
        else
        {
            model.CustomerId = null;
        }

        // --- Discount: free-typed amounts are gone. Only a real, rule-checked code can reduce the sale, and only for
        // roles granted discounts.view. The amount is computed here from database prices - never from the browser. ---
        var discountCode = _discountService.NormalizeCode(model.DiscountCode);
        model.DiscountCode = discountCode.Length == 0 ? null : discountCode;
        if (model.DiscountCode is not null)
        {
            if (!CanUseDiscounts)
            {
                this.ToastError("Your role isn't allowed to apply discounts. Remove the code and try again.");
                return View(nameof(Index), model);
            }

            var evaluation = await _discountService.EvaluateAsync(
                model.DiscountCode,
                model.CartItems.Select(l => new DiscountLine(l.ProductId, l.Quantity, l.UnitPrice)).ToList(),
                DiscountChannel.InStore,
                model.CustomerId);

            if (!evaluation.IsValid)
            {
                this.ToastError($"Discount not applied: {evaluation.Error}");
                return View(nameof(Index), model);
            }

            model.DiscountTotal = evaluation.Amount;
        }
        else
        {
            // No code typed: if an automatic discount covers this basket, it applies on its own (any cashier, no code).
            var auto = await _discountService.EvaluateBestAutoAsync(
                model.CartItems.Select(l => new DiscountLine(l.ProductId, l.Quantity, l.UnitPrice)).ToList(),
                DiscountChannel.InStore,
                model.CustomerId);

            if (auto.IsValid)
            {
                model.DiscountCode = auto.Discount!.Code;
                model.DiscountTotal = auto.Amount;
            }
        }

        // VAT is always recomputed here from the fixed rate - never trusted from the client.
        model.TaxTotal = TaxSettings.CalculateVat(model.SubTotal, model.DiscountTotal);

        var cashierId = _userManager.GetUserId(User);
        var order = new Order();
        List<ProductVariant> updatedVariants;

        try
        {
            order = new Order
            {
                OrderNumber = $"POS-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                OrderType = OrderType.POS,
                Status = OrderStatus.Completed,
                PaymentMethod = model.PaymentMethod,
                CustomerId = model.CustomerId,
                ProcessedByUserId = cashierId,
                SubTotal = model.SubTotal,
                DiscountTotal = model.DiscountTotal,
                DiscountCode = model.DiscountCode,
                TaxTotal = model.TaxTotal,
                GrandTotal = model.GrandTotal,
                DateFulfilled = DateTime.UtcNow
            };

            foreach (var line in model.CartItems)
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

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                // Consume one redemption of the code in the SAME transaction (atomic, limit-checked). If the code ran
                // out or was switched off a moment ago this throws and the whole sale rolls back.
                if (model.DiscountCode is not null)
                    await _discountService.RedeemAsync(model.DiscountCode, order.OrderId, model.CustomerId, model.DiscountTotal);

                // Atomic, conditional stock UPDATEs - joins this transaction. If another till or an online
                // order took the last unit a moment ago, this throws and the whole sale rolls back.
                updatedVariants = await _inventoryService.DecrementStockBatchAsync(
                    model.CartItems.Select(l => (l.VariantId, l.Quantity)));

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = cashierId,
                    Action = "SaleProcessed",
                    Details = $"Processed sale {order.OrderNumber} for {order.GrandTotal:C} ({model.CartItems.Count} line item(s))." +
                              (order.DiscountTotal > 0 ? $" Discount {order.DiscountCode} applied: {order.DiscountTotal:C}." : "")
                });
                await _context.SaveChangesAsync();

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                foreach (var entry in _context.ChangeTracker.Entries()
                             .Where(e => e.Entity is Order or OrderItem or InventoryTransaction or AuditLog or DiscountRedemption)
                             .ToList())
                    entry.State = EntityState.Detached;
                throw;
            }
        }
        catch (DiscountUnavailableException ex)
        {
            this.ToastError($"The discount code {ex.Code} just ran out or was switched off, so the sale wasn't completed. Remove the code (or use another) and try again.");
            return View(nameof(Index), model);
        }
        catch (InsufficientStockException ex)
        {
            this.ToastError($"Not enough stock - only {ex.Available} of '{ex.Label}' left (another sale just took some). Please adjust the quantity.");
            return View(nameof(Index), model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POS checkout failed for cashier {CashierId} with {ItemCount} item(s).", cashierId, model.CartItems.Count);
            this.ToastError("Something went wrong completing the sale. Nothing was charged - please try again.");
            return View(nameof(Index), model);
        }

        // The sale is committed. Everything below is best-effort and must never turn a completed sale into an error.
        var lowStockVariantIds = updatedVariants.Where(v => v.IsLowStock).Select(v => v.ProductVariantId).ToHashSet();
        var newlyLowStock = model.CartItems
            .Where(l => lowStockVariantIds.Contains(l.VariantId))
            .Select(l => $"{l.ProductName} ({l.Size}/{l.Color})")
            .Distinct()
            .ToList();

        var pointsEarned = 0;
        if (!string.IsNullOrWhiteSpace(order.CustomerId))
        {
            try { pointsEarned = (await _rewardsService.EarnForOrderAsync(order)).Points; }
            catch (Exception ex) { _logger.LogError(ex, "Sale {OrderNumber} completed but awarding reward points failed.", order.OrderNumber); }
        }

        // Digital receipt (US-07): an explicit ReceiptEmail typed at the till takes priority (covers walk-in
        // customers with no account); otherwise fall back to the linked customer account's email, if any.
        var recipientEmail = model.ReceiptEmail;
        var recipientName = "there";

        try
        {
            if (string.IsNullOrWhiteSpace(recipientEmail) && !string.IsNullOrWhiteSpace(model.CustomerId))
            {
                var linkedCustomer = await _userManager.FindByIdAsync(model.CustomerId);
                if (linkedCustomer is not null && !string.IsNullOrWhiteSpace(linkedCustomer.Email))
                {
                    recipientEmail = linkedCustomer.Email;
                    recipientName = linkedCustomer.FullName;
                }
            }

            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                var itemsHtml = string.Join("", model.CartItems.Select(l =>
                    $"<tr><td>{WebUtility.HtmlEncode(l.ProductName)}</td><td>{l.Quantity}</td><td>{l.UnitPrice:C}</td><td>{l.LineTotal:C}</td></tr>"));

                var discountLine = order.DiscountTotal > 0 ? $"Discount{(order.DiscountCode is null ? "" : $" ({WebUtility.HtmlEncode(order.DiscountCode)})")}: -{order.DiscountTotal:C}<br/>" : "";
                var body = $@"
                    <h2>Thanks for shopping with us, {WebUtility.HtmlEncode(recipientName)}!</h2>
                    <p>Receipt for order <strong>{order.OrderNumber}</strong> ({order.DateCreated:dd MMM yyyy, HH:mm}).</p>
                    <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                        <thead><tr><th>Item</th><th>Qty</th><th>Unit Price</th><th>Line Total</th></tr></thead>
                        <tbody>{itemsHtml}</tbody>
                    </table>
                    <p>Subtotal: {order.SubTotal:C}<br/>{discountLine}VAT (15%): {order.TaxTotal:C}<br/>
                    <strong>Total: {order.GrandTotal:C}</strong> (paid via {order.PaymentMethod})</p>";

                await _emailSender.SendAsync(recipientEmail, $"Receipt - {order.OrderNumber}", body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sale {OrderNumber} completed but the receipt email failed.", order.OrderNumber);
            recipientEmail = null;
        }

        this.ToastSuccess($"Sale {order.OrderNumber} completed - {order.GrandTotal:C} ({model.CartItems.Count} item(s))." +
            (string.IsNullOrWhiteSpace(recipientEmail) ? "" : $" Receipt emailed to {recipientEmail}.") +
            (pointsEarned > 0 ? $" Customer earned {pointsEarned} points." : ""));

        if (newlyLowStock.Count > 0)
            this.ToastWarning($"Now low on stock: {string.Join(", ", newlyLowStock)}.");

        return RedirectToAction(nameof(Receipt), new { id = order.OrderId });
    }

    // GET: /Pos/Receipt/5
    [HttpGet]
    public async Task<IActionResult> Receipt(int id)
    {
        var order = await _context.Orders
            .Include(o => o.ProcessedByUser)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.ProductVariant)
            .FirstOrDefaultAsync(o => o.OrderId == id);

        if (order is null) return NotFound();
        return View(order);
    }

    // GET: /Pos/Product/{sku} - AJAX lookup used by the barcode-scanning JS in wwwroot/js.
    // "sku" here is now a VARIANT sku (e.g. "CLO-0001-M-BLK") - each size/colour is its own
    // scannable code, rather than one SKU per style.
    [HttpGet]
    public async Task<IActionResult> Product(string sku)
    {
        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.SKU == sku && v.IsActive && v.Product.IsActive)
            .Select(v => new
            {
                ProductId = v.ProductId,
                VariantId = v.ProductVariantId,
                v.Product.Name,
                Sku = v.SKU,
                SellingPrice = v.PriceOverride ?? v.Product.SellingPrice,
                v.StockQuantity,
                v.Product.Category,
                v.Size,
                v.Color,
                v.Product.Brand,
                v.Product.ImageUrl,
                v.Product.Description
            })
            .FirstOrDefaultAsync();

        return variant is null ? NotFound() : Json(variant);
    }
}
