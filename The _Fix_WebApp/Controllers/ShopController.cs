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
    public IActionResult Cart()
    {
        return View(SessionCart.Get(HttpContext.Session));
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


    // GET: /Shop/Checkout
    [HttpGet]
    [Authorize(Roles = "Customer")]
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
    [Authorize(Roles = "Customer")]
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
        // The CART CONTENTS are snapshotted here (see SessionCart.SaveSnapshot) rather than
        // re-read from the live session cart at callback time - that's the fix for the
        // price/quantity-tampering window where a customer could edit their cart in another
        // tab while sitting on Paystack's page. Everything else here is just metadata this
        // attempt belongs to.
        SessionCart.SaveSnapshot(HttpContext.Session, reference, cart);
        HttpContext.Session.SetString("PendingPaymentReference", reference);
        HttpContext.Session.SetString("PendingPaymentMethod", model.PaymentMethod.ToString());
        HttpContext.Session.SetInt32("PendingAddressId", deliveryAddress.CustomerAddressId);
        HttpContext.Session.SetString("PendingSaveCard", model.SaveCard ? "true" : "false");

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

        return View(new ProductDetailViewModel
        {
            Product = product,
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
    public async Task<IActionResult> SubmitReview(int productId, int rating, string? comment)
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

        _context.ProductReviews.Add(new ProductReview
        {
            ProductId = productId,
            CustomerId = userId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            IsVerifiedPurchase = isVerified
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

        this.ToastSuccess("Thanks - your review has been posted.");
        return RedirectToAction(nameof(Product), new { id = productId });
    }
}