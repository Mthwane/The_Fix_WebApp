using System.Globalization;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Controllers;

/// <summary>
/// The customer-facing storefront: browse the catalogue, build a cart, and check out
/// (US-14: "check out and pay for my order using my preferred payment method").
/// Browsing and the cart are open to anonymous visitors (a "visitor" is simply anyone not
/// signed in - there's no separate account for it); Checkout/Confirmation require a
/// Customer account, since an Order has to be linked to somebody. Cart state lives in
/// Session, not the database - only a completed checkout ever writes an Order row, so an
/// abandoned cart never touches stock or the Orders table, and it survives the trip from
/// anonymous browsing straight through to signing in at checkout.
/// </summary>
[AllowAnonymous]
public class ShopController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDiscountService _discounts;
    private readonly ILogger<ShopController> _logger;

    public ShopController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IDiscountService discounts,
        ILogger<ShopController> logger)
    {
        _context = context;
        _userManager = userManager;
        _discounts = discounts;
        _logger = logger;
    }

    // ------------------------------------------------------------------ discount codes (online)

    /// <summary>The signed-in customer's id for per-customer limits (null for anonymous visitors and staff).</summary>
    private string? CurrentCustomerId() =>
        User.Identity?.IsAuthenticated == true && User.IsInRole("Customer") ? _userManager.GetUserId(User) : null;

    private async Task<DiscountLine[]> CartLinesAsync(CartViewModel cart)
    {
        // Line prices are the server-side session cart's own (the same figures the order total is built from), so the
        // saving always matches what the customer is charged. The product id is read from the database, not trusted.
        var ids = cart.Lines.Select(l => l.VariantId).Distinct().ToList();
        var productIds = await _context.ProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId, v => v.ProductId);

        return cart.Lines
            .Where(l => productIds.ContainsKey(l.VariantId))
            .Select(l => new DiscountLine(productIds[l.VariantId], l.Quantity, l.UnitPrice))
            .ToArray();
    }

    /// <summary>Re-checks the typed code in the session against the current cart (dropping it if it no longer works). When no
    /// typed code is in play, falls back to the best automatic discount for the basket - a typed code always wins.</summary>
    private async Task<(string? Code, decimal Amount, string? Error, bool Auto)> ResolveAppliedDiscountAsync(CartViewModel cart)
    {
        if (cart.Lines.Count == 0) return (null, 0m, null, false);

        var lines = await CartLinesAsync(cart);
        string? error = null;

        var code = SessionCart.GetDiscountCode(HttpContext.Session);
        if (!string.IsNullOrWhiteSpace(code))
        {
            var result = await _discounts.EvaluateAsync(code, lines, DiscountChannel.Online, CurrentCustomerId());
            if (result.IsValid)
                return (result.Discount!.Code, result.Amount, null, false);

            SessionCart.ClearDiscountCode(HttpContext.Session);
            error = result.Error;
        }

        var auto = await _discounts.EvaluateBestAutoAsync(lines, DiscountChannel.Online, CurrentCustomerId());
        return auto.IsValid
            ? (auto.Discount!.Code, auto.Amount, error, true)
            : (null, 0m, error, false);
    }

    private async Task LoadDiscountViewDataAsync(CartViewModel cart)
    {
        var (code, amount, error, auto) = await ResolveAppliedDiscountAsync(cart);
        ViewBag.DiscountCode = code;
        ViewBag.DiscountAmount = amount;
        ViewBag.DiscountAuto = auto;
        if (error is not null) this.ToastWarning($"Your discount code was removed: {error}");
    }

    // POST: /Shop/ApplyDiscountCode - works for visitors too (the per-customer limit is re-checked at checkout).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyDiscountCode(string? code, string? returnTo)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        var result = await _discounts.EvaluateAsync(code, await CartLinesAsync(cart), DiscountChannel.Online, CurrentCustomerId());

        if (result.IsValid)
        {
            SessionCart.SetDiscountCode(HttpContext.Session, result.Discount!.Code);
            this.ToastSuccess($"{result.Discount.Code} applied - you save {result.Amount:C} (before VAT).");
        }
        else
        {
            this.ToastError(result.Error ?? "That code can't be used.");
        }

        return returnTo == "checkout" ? RedirectToAction(nameof(Checkout)) : RedirectToAction(nameof(Cart));
    }

    // POST: /Shop/RemoveDiscountCode
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveDiscountCode(string? returnTo)
    {
        SessionCart.ClearDiscountCode(HttpContext.Session);
        this.ToastSuccess("Discount code removed.");
        return returnTo == "checkout" ? RedirectToAction(nameof(Checkout)) : RedirectToAction(nameof(Cart));
    }

    // GET: /Shop/Department/{slug} - a single department's landing page (Women/Men/Footwear/
    // Kids/...), matching the Figma department-page designs. Reuses the same catalogue query
    // as Index, just pre-filtered to this department.
    [HttpGet]
    public async Task<IActionResult> Department(string slug, string? sub, string? size, string? color)
    {
        var department = await _context.Departments
            .AsNoTracking()
            .Include(d => d.SubCategories.OrderBy(s => s.DisplayOrder))
            .FirstOrDefaultAsync(d => d.Slug == slug && d.IsActive);

        if (department is null) return NotFound();

        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Where(p => p.IsActive && p.DepartmentId == department.DepartmentId
                && p.Variants.Any(v => v.IsActive && v.StockQuantity > 0));

        if (!string.IsNullOrWhiteSpace(sub))
        {
            var subCategoryName = department.SubCategories.FirstOrDefault(s => s.Slug == sub)?.Name;
            if (subCategoryName is not null)
                query = query.Where(p => p.SubCategory == subCategoryName);
        }
        if (!string.IsNullOrWhiteSpace(size))
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Size == size));
        if (!string.IsNullOrWhiteSpace(color))
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Color == color));

        var products = await query.OrderByDescending(p => p.DateAdded).ToListAsync();

        return View(new DepartmentPageViewModel
        {
            Department = department,
            Products = products,
            SelectedSubCategory = sub,
            SelectedSize = size,
            SelectedColor = color
        });
    }

    // GET: /Shop - browse the full catalogue. "In stock" now means at least one active variant
    // has stock; each card shows its available sizes/colours so a customer picks one
    // before adding to cart (see AddToCart, keyed to a specific variantId).
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? category)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Where(p => p.IsActive && p.Variants.Any(v => v.IsActive && v.StockQuantity > 0));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.SKU.Contains(search) || p.Variants.Any(v => v.SKU.Contains(search)));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        ViewBag.Categories = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        ViewBag.SearchTerm = search;
        ViewBag.SelectedCategory = category;
        ViewBag.CartItemCount = SessionCart.Get(HttpContext.Session).ItemCount;

        var products = await query.OrderBy(p => p.Name).ToListAsync();
        return View(products);
    }

    // POST: /Shop/AddToCart - now takes the exact variant (size/colour) chosen on the
    // product card/detail page, not just the parent product.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(int variantId, int quantity = 1, string? returnUrl = null)
    {
        IActionResult BackToSource() =>
            !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? Redirect(returnUrl)
                : RedirectToAction(nameof(Index));

        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.ProductVariantId == variantId && v.IsActive && v.Product.IsActive);

        if (variant is null)
        {
            this.ToastError("That size/colour is no longer available.");
            return BackToSource();
        }

        if (variant.StockQuantity <= 0)
        {
            this.ToastError($"'{variant.Product.Name}' ({variant.Size}/{variant.Color}) is out of stock.");
            return BackToSource();
        }

        var cart = SessionCart.Get(HttpContext.Session);
        var line = cart.Lines.FirstOrDefault(l => l.VariantId == variantId);

        var desiredQuantity = (line?.Quantity ?? 0) + Math.Max(1, quantity);
        var capped = desiredQuantity > variant.StockQuantity;
        if (capped) desiredQuantity = variant.StockQuantity; // never let the cart exceed what's actually in stock

        if (line is null)
        {
            cart.Lines.Add(new CartLineViewModel
            {
                ProductId = variant.ProductId,
                VariantId = variant.ProductVariantId,
                Name = variant.Product.Name,
                SKU = variant.SKU,
                Size = variant.Size,
                Color = variant.Color,
                ImageUrl = variant.Product.ImageUrl,
                UnitPrice = variant.EffectivePrice,
                Quantity = desiredQuantity
            });
        }
        else
        {
            line.Quantity = desiredQuantity;
        }

        SessionCart.Save(HttpContext.Session, cart);

        if (capped)
            this.ToastWarning($"Only {variant.StockQuantity} of '{variant.Product.Name}' available - added the max to your cart.");
        else
            this.ToastSuccess($"Added {variant.Product.Name} ({variant.Size}/{variant.Color}) to your cart.");

        return BackToSource();
    }

    // GET: /Shop/Cart
    [HttpGet]
    public async Task<IActionResult> Cart()
    {
        var cart = SessionCart.Get(HttpContext.Session);
        await LoadDiscountViewDataAsync(cart);
        return View(cart);
    }

    // POST: /Shop/UpdateCartLine
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateCartLine(int variantId, int quantity)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        var line = cart.Lines.FirstOrDefault(l => l.VariantId == variantId);

        if (line is not null)
        {
            if (quantity <= 0)
                cart.Lines.Remove(line);
            else
                line.Quantity = quantity;
        }

        SessionCart.Save(HttpContext.Session, cart);
        this.ToastSuccess("Cart updated.");
        return RedirectToAction(nameof(Cart));
    }

    // POST: /Shop/RemoveFromCart
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveFromCart(int variantId)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        cart.Lines.RemoveAll(l => l.VariantId == variantId);
        SessionCart.Save(HttpContext.Session, cart);
        this.ToastSuccess("Item removed from your cart.");
        return RedirectToAction(nameof(Cart));
    }


    // Smallest card payment the gateway will take. Not verified against your Paystack account - set it to
    // your real minimum. (Used when FixCash covers most of an order and only a sliver is left for the card.)
    private const decimal MinCardChargeRands = 1.00m;

    // GET: /Shop/Checkout
    [HttpGet]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Checkout([FromServices] IWalletService wallet, [FromServices] IRewardsService rewards)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        if (cart.Lines.Count == 0) return RedirectToAction(nameof(Index));

        var userId = _userManager.GetUserId(User);
        var model = await BuildCheckoutViewModelAsync(userId!, cart);
        ViewBag.WalletBalance = await wallet.GetBalanceAsync(userId!);
        await LoadRewardsForViewAsync(rewards, userId!, cart);
        await LoadDiscountViewDataAsync(cart);
        return View(model);
    }

    /// <summary>Loads the customer's saved addresses/cards and pre-selects their defaults - shared by the GET and the POST-with-errors path so both show the same picker.</summary>
    private async Task<CheckoutViewModel> BuildCheckoutViewModelAsync(string userId, CartViewModel cart)
    {
        var addresses = await _context.CustomerAddresses
            .AsNoTracking()
            .Where(a => a.CustomerId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Label)
            .ToListAsync();

        var savedCards = await _context.CustomerPaymentMethods
            .AsNoTracking()
            .Where(p => p.CustomerId == userId)
            .OrderByDescending(p => p.IsDefault)
            .ToListAsync();

        return new CheckoutViewModel
        {
            Cart = cart,
            Addresses = addresses,
            SavedCards = savedCards,
            SelectedAddressId = addresses.FirstOrDefault(a => a.IsDefault)?.CustomerAddressId ?? addresses.FirstOrDefault()?.CustomerAddressId,
            SelectedPaymentMethodId = savedCards.FirstOrDefault(p => p.IsDefault)?.CustomerPaymentMethodId ?? savedCards.FirstOrDefault()?.CustomerPaymentMethodId
        };
    }

    private async Task<(RewardsSettings Settings, RedemptionQuote Quote)> LoadRewardsForViewAsync(IRewardsService rewards, string userId, CartViewModel cart)
    {
        var settings = await rewards.GetSettingsAsync();
        var balance = await rewards.GetBalanceAsync(userId);
        var quote = rewards.GetRedemptionQuote(settings, balance, cart.SubTotal);
        ViewBag.RewardsSettings = settings;
        ViewBag.RewardsQuote = quote;
        return (settings, quote);
    }

    private async Task<IActionResult> CheckoutViewWithErrorAsync(CheckoutViewModel model, string userId, CartViewModel cart, string error)
    {
        this.ToastError(error);
        var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
        model.Addresses = rebuilt.Addresses;
        model.SavedCards = rebuilt.SavedCards;
        return View(model);
    }

    // POST: /Shop/Checkout - FixCash wallet, a saved card, or a brand-new card via Paystack (or the wallet
    // for part and a card for the rest). No Order is created until the money has actually moved: the
    // wallet and saved-card paths create it right after the money is taken; the redirect path creates it
    // in PaymentsController.Callback once the customer comes back.
    //
    // If money has moved but the order STILL can't be created, IPaymentRecoveryService hands everything
    // back (wallet, points, and the card via a gateway refund) and records a PaymentIncident.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Checkout(
        CheckoutViewModel model,
        [FromServices] IPaymentService payments,
        [FromServices] IOrderFulfillmentService orderFulfillment,
        [FromServices] IWalletService wallet,
        [FromServices] IRewardsService rewards,
        [FromServices] IPaymentRecoveryService recovery)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        if (cart.Lines.Count == 0) return RedirectToAction(nameof(Index));

        model.Cart = cart;
        var userId = _userManager.GetUserId(User)!;
        var walletBalance = await wallet.GetBalanceAsync(userId);
        ViewBag.WalletBalance = walletBalance;
        var (rewardsSettings, rewardsQuote) = await LoadRewardsForViewAsync(rewards, userId, cart);
        await LoadDiscountViewDataAsync(cart);

        if (!ModelState.IsValid)
            return await CheckoutViewWithErrorAsync(model, userId, cart, "Please choose a delivery address and payment method to complete your order.");

        // Re-check stock before we ever send the customer to pay.
        var checkoutVariantIds = cart.Lines.Select(l => l.VariantId).Distinct().ToList();
        var checkoutVariants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => checkoutVariantIds.Contains(v.ProductVariantId))
            .ToDictionaryAsync(v => v.ProductVariantId);

        foreach (var line in cart.Lines)
        {
            if (!checkoutVariants.TryGetValue(line.VariantId, out var variant) || !variant.IsActive || !variant.Product.IsActive)
                ModelState.AddModelError(string.Empty, $"'{line.Name}' is no longer available. Please remove it from your cart.");
            else if (variant.StockQuantity < line.Quantity)
                ModelState.AddModelError(string.Empty, $"Only {variant.StockQuantity} of '{line.Name}' ({variant.Size}/{variant.Color}) left in stock - please update the quantity.");
        }

        if (!ModelState.IsValid)
            return await CheckoutViewWithErrorAsync(model, userId, cart, "Some items in your cart changed - please review and try again.");

        var user = await _userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return await CheckoutViewWithErrorAsync(model, userId, cart, "Your account needs a valid email address before you can pay online.");

        var deliveryAddress = await _context.CustomerAddresses
            .FirstOrDefaultAsync(a => a.CustomerAddressId == model.SelectedAddressId && a.CustomerId == userId);
        if (deliveryAddress is null)
            return await CheckoutViewWithErrorAsync(model, userId, cart, "Please choose (or add) a delivery address before checking out.");

        // --- Reward points: the discount comes off first ---
        var pointsToRedeem = 0;
        var pointsDiscount = 0m;
        if (model.UsePoints)
        {
            if (!rewardsQuote.CanRedeem)
                return await CheckoutViewWithErrorAsync(model, userId, cart, rewardsQuote.Reason ?? "Your points can't be applied to this order.");

            pointsToRedeem = rewardsQuote.MaxPoints;
            pointsDiscount = rewards.PointsToRands(rewardsSettings, pointsToRedeem);
        }

        // --- Discount code: re-validated right now (limits, dates, caps, linked products), amount from the server ---
        var discountCode = (string?)ViewBag.DiscountCode;
        var codeDiscount = discountCode is null ? 0m : (decimal)ViewBag.DiscountAmount;
        if (discountCode is not null)
        {
            var codeCheck = await _discounts.EvaluateAsync(discountCode, await CartLinesAsync(cart), DiscountChannel.Online, userId);
            if (!codeCheck.IsValid)
            {
                SessionCart.ClearDiscountCode(HttpContext.Session);
                return await CheckoutViewWithErrorAsync(model, userId, cart, $"Your discount code can't be used: {codeCheck.Error}");
            }
            codeDiscount = codeCheck.Amount;
        }

        if (codeDiscount + pointsDiscount > cart.SubTotal)
            return await CheckoutViewWithErrorAsync(model, userId, cart, "Your discount code and reward points together are worth more than your basket. Untick the points or remove the code.");

        var totalDiscount = codeDiscount + pointsDiscount;
        var vat = TaxSettings.CalculateVat(cart.SubTotal, totalDiscount);
        var deliveryFee = DeliverySettings.CalculateFee(cart.SubTotal); // flat R100 under R500, free from R500
        var grandTotal = cart.SubTotal - totalDiscount + vat + deliveryFee;
        var reference = $"WEB-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

        // --- FixCash wallet: covers as much of the total as the balance allows, the card pays the rest ---
        var useWallet = model.UseWallet || model.PaymentMethod == PaymentMethod.FixCash;
        var walletPortion = 0m;
        if (useWallet)
        {
            walletPortion = Math.Round(Math.Min(walletBalance, grandTotal), 2, MidpointRounding.AwayFromZero);
            if (walletPortion <= 0)
                return await CheckoutViewWithErrorAsync(model, userId, cart, "Your FixCash balance is empty - add funds or pay by card.");
        }

        var cardPortion = grandTotal - walletPortion;
        if (cardPortion > 0m && cardPortion < MinCardChargeRands)
            return await CheckoutViewWithErrorAsync(model, userId, cart,
                $"After FixCash, {cardPortion:C} would be left to pay by card, which is below the minimum card payment. Please add funds to your wallet first.");

        var paysFullyFromWallet = useWallet && cardPortion <= 0m;
        // "FixCash" is a funding source, not a card type - a part-paid order records the card method used.
        var cardMethod = model.PaymentMethod == PaymentMethod.FixCash ? PaymentMethod.CreditCard : model.PaymentMethod;

        async Task<string?> TryRedeemPointsAsync()
        {
            if (pointsToRedeem <= 0) return null;
            var r = await rewards.RedeemAsync(userId, pointsToRedeem, reference);
            return r.Success ? null : $"Could not apply your points: {r.ErrorMessage}";
        }

        // Used while NOTHING has been charged on the card yet (wallet/points can simply be put back).
        async Task RollbackAsync(bool walletDebited)
        {
            if (walletDebited) await wallet.RestoreDebitAsync(userId, reference);
            if (pointsToRedeem > 0) await rewards.RestoreRedemptionAsync(userId, reference);
        }

        // Money HAS moved but the order could not be created - give everything back and tell the customer.
        async Task<IActionResult> OrderFailedAfterPaymentAsync(PaymentIncidentSource source, decimal cardCharged, decimal walletDebited, Exception ex)
        {
            var outcome = await recovery.HandleFailedOrderAsync(new PaymentFailureContext
            {
                Reference = reference,
                CustomerId = userId,
                CustomerEmail = user.Email,
                CustomerName = user.FullName,
                Source = source,
                CardCharged = cardCharged,
                WalletDebited = walletDebited,
                PointsRedeemed = pointsToRedeem,
                Reason = ex is InsufficientStockException ? $"Sold out while paying: {ex.Message}" : $"Order creation failed: {ex.Message}"
            });

            this.ToastError(outcome.CustomerMessage);
            return RedirectToAction(nameof(Cart));
        }

        // --- Path 0: the wallet pays for everything - no card, no redirect. Debit BEFORE creating the order. ---
        if (paysFullyFromWallet)
        {
            var pointsError = await TryRedeemPointsAsync();
            if (pointsError is not null) return await CheckoutViewWithErrorAsync(model, userId, cart, pointsError);

            var debitResult = await wallet.DebitForOrderAsync(userId, walletPortion, reference);
            if (!debitResult.Success)
            {
                await RollbackAsync(false);
                return await CheckoutViewWithErrorAsync(model, userId, cart, $"Could not pay with FixCash: {debitResult.ErrorMessage}");
            }

            Order order;
            try
            {
                order = await orderFulfillment.CreateOnlineOrderAsync(user, cart, PaymentMethod.FixCash, reference, deliveryAddress, pointsDiscount, walletPortion, discountCode, codeDiscount);
            }
            catch (Exception ex)
            {
                return await OrderFailedAfterPaymentAsync(PaymentIncidentSource.WalletCheckout, 0m, walletPortion, ex);
            }
            await wallet.LinkOrderAsync(reference, order.OrderId);

            SessionCart.Clear(HttpContext.Session);
            this.ToastSuccess($"Paid with FixCash - order {order.OrderNumber} placed for {order.GrandTotal:C}.");
            return RedirectToAction(nameof(Confirmation), new { id = order.OrderId });
        }

        // --- Path 1: saved card (optionally after a FixCash part-payment) ---
        if (model.SelectedPaymentMethodId.HasValue)
        {
            var savedCard = await _context.CustomerPaymentMethods
                .FirstOrDefaultAsync(p => p.CustomerPaymentMethodId == model.SelectedPaymentMethodId && p.CustomerId == userId);
            if (savedCard is null)
                return await CheckoutViewWithErrorAsync(model, userId, cart, "That saved card is no longer available - please choose another or add a new one.");

            var pointsError = await TryRedeemPointsAsync();
            if (pointsError is not null) return await CheckoutViewWithErrorAsync(model, userId, cart, pointsError);

            var walletDebited = false;
            if (walletPortion > 0m)
            {
                var debitResult = await wallet.DebitForOrderAsync(userId, walletPortion, reference);
                if (!debitResult.Success)
                {
                    await RollbackAsync(false);
                    return await CheckoutViewWithErrorAsync(model, userId, cart, $"Could not use your FixCash balance: {debitResult.ErrorMessage}");
                }
                walletDebited = true;
            }

            var chargeResult = await payments.ChargeAuthorizationAsync(user.Email, cardPortion, savedCard.AuthorizationCode, reference);
            if (!chargeResult.Success)
            {
                await RollbackAsync(walletDebited); // the wallet and points go back if the card is declined
                return await CheckoutViewWithErrorAsync(model, userId, cart, $"Your saved card was declined: {chargeResult.ErrorMessage}. Please try another card.");
            }

            Order order;
            try
            {
                order = await orderFulfillment.CreateOnlineOrderAsync(user, cart, cardMethod, reference, deliveryAddress, pointsDiscount, walletPortion, discountCode, codeDiscount);
            }
            catch (Exception ex)
            {
                // The card WAS charged - this is the case that used to lose the customer's money.
                return await OrderFailedAfterPaymentAsync(PaymentIncidentSource.SavedCardCheckout, cardPortion, walletDebited ? walletPortion : 0m, ex);
            }

            if (walletDebited) await wallet.LinkOrderAsync(reference, order.OrderId);
            SessionCart.Clear(HttpContext.Session);

            this.ToastSuccess($"Payment confirmed - order {order.OrderNumber} placed for {order.GrandTotal:C}.");
            return RedirectToAction(nameof(Confirmation), new { id = order.OrderId });
        }

        // --- Path 2: new card via Paystack. Only the CARD portion goes to Paystack; the wallet portion and
        // points are taken in PaymentsController.Callback once the card payment is verified. ---
        var callbackUrl = Url.Action(nameof(PaymentsController.Callback), "Payments", null, Request.Scheme)!;
        var initResult = await payments.InitializeTransactionAsync(user.Email, cardPortion, reference, callbackUrl);
        if (!initResult.Success)
            return await CheckoutViewWithErrorAsync(model, userId, cart, $"Could not start payment: {initResult.ErrorMessage}");

        SessionCart.SaveSnapshot(HttpContext.Session, reference, cart);
        HttpContext.Session.SetString("PendingPaymentReference", reference);
        HttpContext.Session.SetString("PendingPaymentMethod", cardMethod.ToString());
        HttpContext.Session.SetInt32("PendingAddressId", deliveryAddress.CustomerAddressId);
        HttpContext.Session.SetString("PendingSaveCard", model.SaveCard ? "true" : "false");
        HttpContext.Session.SetInt32("PendingPointsRedeemed", pointsToRedeem);
        HttpContext.Session.SetString("PendingPointsDiscount", pointsDiscount.ToString("F2", CultureInfo.InvariantCulture));
        HttpContext.Session.SetString("PendingDiscountCode", discountCode ?? string.Empty);
        HttpContext.Session.SetString("PendingDiscountAmount", codeDiscount.ToString("F2", CultureInfo.InvariantCulture));
        HttpContext.Session.SetString("PendingWalletAmount", walletPortion.ToString("F2", CultureInfo.InvariantCulture));

        return Redirect(initResult.AuthorizationUrl!);
    }

    // GET: /Shop/Confirmation/5
    [HttpGet]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Confirmation(int id)
    {
        var userId = _userManager.GetUserId(User);

        var order = await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.ProductVariant)
            .FirstOrDefaultAsync(o => o.OrderId == id && o.CustomerId == userId);

        if (order is null) return NotFound();
        return View(order);
    }

    // GET: /Shop/Product/5 - a single style's detail page (gallery, variant picker, reviews).
    [HttpGet]
    public async Task<IActionResult> Product(int id)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Department)
            .FirstOrDefaultAsync(p => p.ProductId == id && p.IsActive);

        if (product is null) return NotFound();

        var images = await _context.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == id)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync();

        var reviews = await _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.ProductId == id)
            .OrderByDescending(r => r.DateCreated)
            .ToListAsync();

        var isWishlisted = false;
        var canReview = false;
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Customer"))
        {
            var userId = _userManager.GetUserId(User);
            isWishlisted = await _context.WishlistItems.AnyAsync(w => w.CustomerId == userId && w.ProductId == id);
            canReview = !reviews.Any(r => r.CustomerId == userId);
        }

        var linkedDiscounts = await _discounts.GetLinkedBannerDiscountsAsync(product.ProductId, product.Category, product.Brand, product.SupplierId);

        return View(new ProductDetailViewModel
        {
            Product = product,
            LinkedDiscounts = linkedDiscounts,
            Images = images,
            Reviews = reviews,
            IsWishlisted = isWishlisted,
            CanReview = canReview,
            ReturnUrl = Url.Action(nameof(Product), new { id })
        });
    }

    // POST: /Shop/SubmitReview - one review per customer per product. Recalculates the
    // product's denormalized AverageRating/ReviewCount inline (no separate reviews service
    // yet - see the TODO on ProductReview for the eventual IReviewService).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> SubmitReview(int productId, int rating, string? comment, List<IFormFile>? photos, [FromServices] FashionFix.Web.Services.Images.IImageStorage imageStorage)
    {
        var userId = _userManager.GetUserId(User)!;

        if (rating < 1 || rating > 5)
        {
            this.ToastError("Please choose a rating between 1 and 5 stars.");
            return RedirectToAction(nameof(Product), new { id = productId });
        }

        var alreadyReviewed = await _context.ProductReviews.AnyAsync(r => r.ProductId == productId && r.CustomerId == userId);
        if (alreadyReviewed)
        {
            this.ToastWarning("You've already reviewed this product.");
            return RedirectToAction(nameof(Product), new { id = productId });
        }

        // "Verified Purchase" = this customer has a Delivered order containing this product.
        var isVerified = await _context.OrderItems
            .AnyAsync(oi => oi.ProductId == productId
                && oi.Order.CustomerId == userId
                && oi.Order.Status == OrderStatus.Delivered);

        // Optional photos (max 3). A bad file is skipped with a warning rather than losing the whole review.
        var photoUrls = new List<string>();
        var skipped = 0;
        foreach (var file in (photos ?? new List<IFormFile>()).Where(f => f.Length > 0).Take(3))
        {
            var upload = await imageStorage.UploadAsync(file, "reviews");
            if (upload.Success && !string.IsNullOrWhiteSpace(upload.Url)) photoUrls.Add(upload.Url!);
            else skipped++;
        }

        _context.ProductReviews.Add(new ProductReview
        {
            ProductId = productId,
            CustomerId = userId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            IsVerifiedPurchase = isVerified,
            PhotoUrls = photoUrls.Count > 0 ? string.Join("|", photoUrls) : null
        });
        await _context.SaveChangesAsync();

        // Recalculate the product's denormalized rating fields from the review table.
        var product = await _context.Products.FirstAsync(p => p.ProductId == productId);
        var stats = await _context.ProductReviews
            .Where(r => r.ProductId == productId)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => r.Rating) })
            .FirstAsync();
        product.AverageRating = Math.Round(stats.Average, 1);
        product.ReviewCount = stats.Count;
        await _context.SaveChangesAsync();

        if (skipped > 0) this.ToastWarning($"Your review was posted, but {skipped} photo(s) could not be uploaded (use JPG, PNG or WebP under 5 MB).");
        else this.ToastSuccess("Thanks - your review has been posted.");
        return RedirectToAction(nameof(Product), new { id = productId });
    }
}
