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
/// Cart state lives in Session, not the database - only a completed checkout ever writes
/// an Order row, so an abandoned cart never touches stock or the Orders table.
/// </summary>
[Authorize(Roles = "Customer")]
public class ShopController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ShopController> _logger;

    public ShopController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger<ShopController> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Shop - browse the catalogue.
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? category)
    {
        var query = _context.Products.AsNoTracking().Where(p => p.IsActive && p.StockQuantity > 0);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.SKU.Contains(search));

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

    // POST: /Shop/AddToCart
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(int productId, int quantity = 1)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive);
        if (product is null)
        {
            this.ToastError("That product is no longer available.");
            return RedirectToAction(nameof(Index));
        }

        if (product.StockQuantity <= 0)
        {
            this.ToastError($"'{product.Name}' is out of stock.");
            return RedirectToAction(nameof(Index));
        }

        var cart = SessionCart.Get(HttpContext.Session);
        var line = cart.Lines.FirstOrDefault(l => l.ProductId == productId);

        var desiredQuantity = (line?.Quantity ?? 0) + Math.Max(1, quantity);
        var capped = desiredQuantity > product.StockQuantity;
        if (capped) desiredQuantity = product.StockQuantity; // never let the cart exceed what's actually in stock

        if (line is null)
        {
            cart.Lines.Add(new CartLineViewModel
            {
                ProductId = product.ProductId,
                Name = product.Name,
                SKU = product.SKU,
                ImageUrl = product.ImageUrl,
                UnitPrice = product.SellingPrice,
                Quantity = desiredQuantity
            });
        }
        else
        {
            line.Quantity = desiredQuantity;
        }

        SessionCart.Save(HttpContext.Session, cart);

        if (capped)
            this.ToastWarning($"Only {product.StockQuantity} of '{product.Name}' available - added the max to your cart.");
        else
            this.ToastSuccess($"Added {product.Name} to your cart.");

        return RedirectToAction(nameof(Index));
    }

    // GET: /Shop/Cart
    [HttpGet]
    public IActionResult Cart()
    {
        return View(SessionCart.Get(HttpContext.Session));
    }

    // POST: /Shop/UpdateCartLine
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateCartLine(int productId, int quantity)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        var line = cart.Lines.FirstOrDefault(l => l.ProductId == productId);

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
    public IActionResult RemoveFromCart(int productId)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        cart.Lines.RemoveAll(l => l.ProductId == productId);
        SessionCart.Save(HttpContext.Session, cart);
        this.ToastSuccess("Item removed from your cart.");
        return RedirectToAction(nameof(Cart));
    }


    // GET: /Shop/Checkout
    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var cart = SessionCart.Get(HttpContext.Session);
        if (cart.Lines.Count == 0) return RedirectToAction(nameof(Index));

        var userId = _userManager.GetUserId(User);
        var model = await BuildCheckoutViewModelAsync(userId!, cart);
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

    // POST: /Shop/Checkout - either charges a saved card instantly, or hands off to Paystack
    // for a brand-new one. Either way, no Order is created here until the money has actually
    // moved: the instant-charge path creates it right after Paystack confirms success; the
    // redirect path creates it in PaymentsController.Callback once the customer comes back.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model, [FromServices] IPaymentService payments, [FromServices] IOrderFulfillmentService orderFulfillment)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        if (cart.Lines.Count == 0) return RedirectToAction(nameof(Index));

        model.Cart = cart;
        var userId = _userManager.GetUserId(User)!;

        if (!ModelState.IsValid)
        {
            var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
            model.Addresses = rebuilt.Addresses;
            model.SavedCards = rebuilt.SavedCards;
            this.ToastError("Please choose a delivery address and payment method to complete your order.");
            return View(model);
        }

        // Re-check stock before we ever send the customer to pay - no point charging them
        // for something that's gone. One query for the whole cart instead of one per line.
        var checkoutProductIds = cart.Lines.Select(l => l.ProductId).Distinct().ToList();
        var checkoutProducts = await _context.Products
            .AsNoTracking()
            .Where(p => checkoutProductIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId);

        foreach (var line in cart.Lines)
        {
            if (!checkoutProducts.TryGetValue(line.ProductId, out var product) || !product.IsActive)
                ModelState.AddModelError(string.Empty, $"'{line.Name}' is no longer available. Please remove it from your cart.");
            else if (product.StockQuantity < line.Quantity)
                ModelState.AddModelError(string.Empty, $"Only {product.StockQuantity} of '{line.Name}' left in stock - please update the quantity.");
        }

        if (!ModelState.IsValid)
        {
            var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
            model.Addresses = rebuilt.Addresses;
            model.SavedCards = rebuilt.SavedCards;
            this.ToastError("Some items in your cart changed - please review and try again.");
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            this.ToastError("Your account needs a valid email address before you can pay online.");
            var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
            model.Addresses = rebuilt.Addresses;
            model.SavedCards = rebuilt.SavedCards;
            return View(model);
        }

        var deliveryAddress = await _context.CustomerAddresses
            .FirstOrDefaultAsync(a => a.CustomerAddressId == model.SelectedAddressId && a.CustomerId == userId);
        if (deliveryAddress is null)
        {
            this.ToastError("Please choose (or add) a delivery address before checking out.");
            var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
            model.Addresses = rebuilt.Addresses;
            model.SavedCards = rebuilt.SavedCards;
            return View(model);
        }

        var vat = TaxSettings.CalculateVat(cart.SubTotal);
        var grandTotal = cart.SubTotal + vat;
        var reference = $"WEB-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

        // --- Path 1: paying with a card already on file - charge it directly, no redirect,
        // no re-entering card details at all. ---
        if (model.SelectedPaymentMethodId.HasValue)
        {
            var savedCard = await _context.CustomerPaymentMethods
                .FirstOrDefaultAsync(p => p.CustomerPaymentMethodId == model.SelectedPaymentMethodId && p.CustomerId == userId);

            if (savedCard is null)
            {
                this.ToastError("That saved card is no longer available - please choose another or add a new one.");
                var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
                model.Addresses = rebuilt.Addresses;
                model.SavedCards = rebuilt.SavedCards;
                return View(model);
            }

            var chargeResult = await payments.ChargeAuthorizationAsync(user.Email, grandTotal, savedCard.AuthorizationCode, reference);

            if (!chargeResult.Success)
            {
                this.ToastError($"Your saved card was declined: {chargeResult.ErrorMessage}. Please try another card.");
                var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
                model.Addresses = rebuilt.Addresses;
                model.SavedCards = rebuilt.SavedCards;
                return View(model);
            }

            var order = await orderFulfillment.CreateOnlineOrderAsync(user, cart, model.PaymentMethod, reference, deliveryAddress);
            SessionCart.Clear(HttpContext.Session);

            this.ToastSuccess($"Payment confirmed - order {order.OrderNumber} placed for {order.GrandTotal:C}.");
            return RedirectToAction(nameof(Confirmation), new { id = order.OrderId });
        }

        // --- Path 2: paying with a brand-new card - hand off to Paystack's hosted page as
        // before. The Order only gets created in PaymentsController.Callback once Paystack
        // confirms the payment actually went through. ---
        var callbackUrl = Url.Action(nameof(PaymentsController.Callback), "Payments", null, Request.Scheme)!;
        var initResult = await payments.InitializeTransactionAsync(user.Email, grandTotal, reference, callbackUrl);

        if (!initResult.Success)
        {
            this.ToastError($"Could not start payment: {initResult.ErrorMessage}");
            var rebuilt = await BuildCheckoutViewModelAsync(userId, cart);
            model.Addresses = rebuilt.Addresses;
            model.SavedCards = rebuilt.SavedCards;
            return View(model);
        }

        // Stash what the callback will need to rebuild the order once payment is verified.
        // The cart itself is already in Session - we just remember which payment method,
        // delivery address, reference, and "save this card?" choice this attempt belongs to.
        HttpContext.Session.SetString("PendingPaymentReference", reference);
        HttpContext.Session.SetString("PendingPaymentMethod", model.PaymentMethod.ToString());
        HttpContext.Session.SetInt32("PendingAddressId", deliveryAddress.CustomerAddressId);
        HttpContext.Session.SetString("PendingSaveCard", model.SaveCard ? "true" : "false");

        return Redirect(initResult.AuthorizationUrl!);
    }
    // GET: /Shop/Confirmation/5
    [HttpGet]
    public async Task<IActionResult> Confirmation(int id)
    {
        var userId = _userManager.GetUserId(User);

        var order = await _context.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.OrderId == id && o.CustomerId == userId);

        if (order is null) return NotFound();
        return View(order);
    }
}