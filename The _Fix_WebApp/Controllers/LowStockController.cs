using FashionFix.Web.Data;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// The staff low-stock queue. Gated by its own permission (LowStockView) so seeing what is running low is separate
/// from raising purchase orders (PurchaseOrdersManage) and approving them (PurchaseOrdersApprove). The Restock button
/// on each row only appears for people who can also raise a purchase order.
/// </summary>
[Authorize(Policy = Permissions.LowStockAccessPolicy)]
public class LowStockController : Controller
{
    private readonly ApplicationDbContext _context;

    public LowStockController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /LowStock - one row per low size/colour, most urgent first, with its supplier.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product).ThenInclude(p => p.Supplier)
            .Where(v => v.IsActive && v.Product.IsActive && v.StockQuantity <= v.Product.LowStockThreshold)
            .OrderBy(v => v.StockQuantity)
            .ThenBy(v => v.Product.Name)
            .ToListAsync();

        ViewBag.CanRaise = User.HasClaim(Permissions.ClaimType, Permissions.PurchaseOrdersManage);
        return View(variants);
    }
}
