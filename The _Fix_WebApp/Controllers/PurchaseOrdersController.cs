using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>Create and monitor purchase orders with suppliers (US-20).</summary>
[Authorize(Roles = "Administrator,Manager,Owner")]
public class PurchaseOrdersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PurchaseOrdersController(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _inventoryService = inventoryService;
        _userManager = userManager;
    }

    // GET: /PurchaseOrders
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var purchaseOrders = await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Items).ThenInclude(i => i.Product)
            .OrderByDescending(po => po.DateCreated)
            .ToListAsync();

        return View(purchaseOrders);
    }

    // GET: /PurchaseOrders/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var suppliers = await _context.Suppliers.OrderBy(s => s.Name).ToListAsync();
        if (!suppliers.Any())
        {
            TempData["PoError"] = "Add a supplier before raising a purchase order.";
            return RedirectToAction("Create", "Suppliers");
        }

        await LoadFormData();
        return View(new PurchaseOrderViewModel());
    }

    // POST: /PurchaseOrders/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseOrderViewModel model)
    {
        model.Lines = model.Lines?.Where(l => l.ProductId != 0 && l.QuantityOrdered > 0).ToList() ?? new();

        if (model.Lines.Count == 0)
            ModelState.AddModelError(string.Empty, "Add at least one product line to the purchase order.");

        if (!ModelState.IsValid)
        {
            await LoadFormData();
            return View(model);
        }

        var purchaseOrder = new PurchaseOrder
        {
            PONumber = $"PO-{DateTime.UtcNow:yyyyMMddHHmmss}",
            SupplierId = model.SupplierId,
            CreatedByUserId = _userManager.GetUserId(User) ?? string.Empty,
            Status = PurchaseOrderStatus.Pending,
            DateExpected = model.DateExpected
        };

        foreach (var line in model.Lines)
        {
            purchaseOrder.Items.Add(new PurchaseOrderItem
            {
                ProductId = line.ProductId,
                QuantityOrdered = line.QuantityOrdered,
                UnitCost = line.UnitCost
            });
        }

        _context.PurchaseOrders.Add(purchaseOrder);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = purchaseOrder.CreatedByUserId,
            Action = "PurchaseOrderCreated",
            Details = $"Raised {purchaseOrder.PONumber} with {purchaseOrder.Items.Count} line item(s)."
        });

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // POST: /PurchaseOrders/Receive/5 - receiving a shipment updates inventory automatically,
    // instead of requiring manual stock entry.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id)
    {
        var purchaseOrder = await _context.PurchaseOrders
            .Include(po => po.Items)
            .FirstOrDefaultAsync(po => po.PurchaseOrderId == id);

        if (purchaseOrder is null) return NotFound();
        if (purchaseOrder.Status == PurchaseOrderStatus.Received) return RedirectToAction(nameof(Index));

        purchaseOrder.Status = PurchaseOrderStatus.Received;
        purchaseOrder.DateReceived = DateTime.UtcNow;

        foreach (var item in purchaseOrder.Items)
        {
            item.QuantityReceived = item.QuantityOrdered;
        }

        await _context.SaveChangesAsync();

        foreach (var item in purchaseOrder.Items)
        {
            await _inventoryService.IncrementStockAsync(
                item.ProductId, item.QuantityOrdered, InventoryChangeReason.PurchaseOrderReceived);
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "PurchaseOrderReceived",
            Details = $"Received {purchaseOrder.PONumber} - stock updated for {purchaseOrder.Items.Count} product(s)."
        });
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    private async Task LoadFormData()
    {
        ViewBag.Suppliers = await _context.Suppliers.OrderBy(s => s.Name).ToListAsync();
        ViewBag.Products = await _context.Products.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
    }
}
