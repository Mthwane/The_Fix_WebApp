using FashionFix.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>Create and monitor purchase orders with suppliers (US-20).</summary>
[Authorize(Roles = "Administrator,Manager,Owner")]
public class PurchaseOrdersController : Controller
{
    private readonly ApplicationDbContext _context;

    public PurchaseOrdersController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /PurchaseOrders
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var purchaseOrders = await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .OrderByDescending(po => po.DateCreated)
            .ToListAsync();

        return View(purchaseOrders);
    }

    // GET: /PurchaseOrders/Create
    [HttpGet]
    public IActionResult Create()
    {
        // TODO: build a PurchaseOrderViewModel (Supplier picker + product/qty/cost lines).
        return View();
    }

    // POST: /PurchaseOrders/Receive/5 - receiving a shipment updates inventory automatically.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id)
    {
        // TODO: mark PurchaseOrder as Received, set QuantityReceived per line,
        // then call IInventoryService.IncrementStockAsync for each product.
        await Task.CompletedTask;
        return RedirectToAction(nameof(Index));
    }
}
