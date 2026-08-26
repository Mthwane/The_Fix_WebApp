using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly IInventoryService _inventoryService;
    private readonly IEmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ShopController> _logger;

    public ShopController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IEmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        ILogger<ShopController> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailSender = emailSender;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Shop - browse the catalogue.
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? category)
    {
        var query = _context.Products.Where(p => p.IsActive && p.StockQuantity > 0);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.SKU.Contains(search));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        ViewBag.Categories = await _context.Products
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
        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive);
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
    public IActionResult Checkout()
    {
        var cart = SessionCart.Get(HttpContext.Session);
        if (cart.Lines.Count == 0) return RedirectToAction(nameof(Index));

        return View(new CheckoutViewModel { Cart = cart });
    }

    // POST: /Shop/Checkout - creates the Order, decrements stock, emails a confirmation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model)
    {
        var cart = SessionCart.Get(HttpContext.Session);
        if (cart.Lines.Count == 0) return RedirectToAction(nameof(Index));

        model.Cart = cart;

        if (!ModelState.IsValid)
        {
            this.ToastError("Please choose a payment method to complete your order.");
            return View(model);
        }

        // Re-check stock at the moment of purchase - it may have moved since the item was
        // added to the cart (another sale, a deactivation, etc).
        foreach (var line in cart.Lines)
        {
            var product = await _context.Products.FindAsync(line.ProductId);
            if (product is null || !product.IsActive)
                ModelState.AddModelError(string.Empty, $"'{line.Name}' is no longer available. Please remove it from your cart.");
            else if (product.StockQuantity < line.Quantity)
                ModelState.AddModelError(string.Empty, $"Only {product.StockQuantity} of '{line.Name}' left in stock - please update the quantity.");
        }

        if (!ModelState.IsValid)
        {
            this.ToastError("Some items in your cart changed - please review and try again.");
            return View(model);
        }

        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.GetUserAsync(User);

        try
        {
            var vat = TaxSettings.CalculateVat(cart.SubTotal);

            var order = new Order
            {
                OrderNumber = $"WEB-{DateTime.UtcNow:yyyyMMddHHmmss}",
                OrderType = OrderType.Online,
                Status = OrderStatus.Processing,
                PaymentMethod = model.PaymentMethod,
                CustomerId = userId,
                SubTotal = cart.SubTotal,
                DiscountTotal = 0,
                TaxTotal = vat,
                GrandTotal = cart.SubTotal + vat
            };

            foreach (var line in cart.Lines)
            {
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineTotal = line.LineTotal
                });
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            var newlyLowStock = new List<string>();
            foreach (var line in cart.Lines)
            {
                await _inventoryService.DecrementStockAsync(line.ProductId, line.Quantity);
                if (await _inventoryService.IsLowStockAsync(line.ProductId))
                    newlyLowStock.Add(line.Name);
            }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "OnlineOrderPlaced",
                Details = $"Placed order {order.OrderNumber} for {order.GrandTotal:C} ({cart.Lines.Count} line item(s))."
            });
            await _context.SaveChangesAsync();

            SessionCart.Clear(HttpContext.Session);

            if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
            {
                var itemsHtml = string.Join("", cart.Lines.Select(l =>
                    $"<tr><td>{l.Name}</td><td>{l.Quantity}</td><td>{l.UnitPrice:C}</td><td>{l.LineTotal:C}</td></tr>"));

                var body = $@"
                    <h2>Thanks for your order, {user.FullName}!</h2>
                    <p>Order <strong>{order.OrderNumber}</strong> has been received and is being processed.</p>
                    <table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                        <thead><tr><th>Item</th><th>Qty</th><th>Unit Price</th><th>Line Total</th></tr></thead>
                        <tbody>{itemsHtml}</tbody>
                    </table>
                    <p>Subtotal: {order.SubTotal:C}<br/>VAT (15%): {order.TaxTotal:C}<br/>
                    <strong>Total: {order.GrandTotal:C}</strong></p>
                    <p>You can track this order any time under My Orders.</p>";

                await _emailSender.SendAsync(user.Email, $"Order Confirmation - {order.OrderNumber}", body);
            }

            this.ToastSuccess($"Order {order.OrderNumber} placed - {order.GrandTotal:C}. A confirmation email is on its way.");

            if (newlyLowStock.Count > 0)
                _logger.LogInformation("Online order pushed these products into low stock: {Products}", string.Join(", ", newlyLowStock));

            return RedirectToAction(nameof(Confirmation), new { id = order.OrderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Online checkout failed for customer {UserId} with {ItemCount} item(s).", userId, cart.Lines.Count);
            this.ToastError("Something went wrong placing your order. You have not been charged - please try again.");
            return View(model);
        }
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
