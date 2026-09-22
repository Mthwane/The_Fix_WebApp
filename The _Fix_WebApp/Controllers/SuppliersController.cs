using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Vendor master data. The address fields aren't just record-keeping - they become the courier
/// collection address when an approved purchase order is booked for collection, so an incomplete
/// address blocks that booking (surfaced on the PO screen rather than failing silently).
/// </summary>
[Authorize(Policy = Permissions.SuppliersManage)]
public class SuppliersController : Controller
{
    private readonly ApplicationDbContext _context;

    public SuppliersController(ApplicationDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var suppliers = await _context.Suppliers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        ViewBag.OpenPoCounts = await _context.PurchaseOrders
            .Where(p => p.Status != PurchaseOrderStatus.Received && p.Status != PurchaseOrderStatus.Cancelled)
            .GroupBy(p => p.SupplierId)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Count);

        return View(suppliers);
    }

    [HttpGet]
    public IActionResult Create() => View(new Supplier());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Supplier model)
    {
        if (!ModelState.IsValid) return View(model);

        _context.Suppliers.Add(model);
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Supplier '{model.Name}' added.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var supplier = await _context.Suppliers.FindAsync(id);
        return supplier is null ? NotFound() : View(supplier);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Supplier model)
    {
        if (id != model.SupplierId) return BadRequest();
        if (!ModelState.IsValid) return View(model);

        var supplier = await _context.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound();

        supplier.Name = model.Name;
        supplier.ContactName = model.ContactName;
        supplier.ContactEmail = model.ContactEmail;
        supplier.ContactPhone = model.ContactPhone;
        supplier.LeadTimeDays = model.LeadTimeDays;
        supplier.StreetAddress = model.StreetAddress;
        supplier.LocalArea = model.LocalArea;
        supplier.City = model.City;
        supplier.Province = model.Province;
        supplier.PostalCode = model.PostalCode;
        supplier.IsActive = model.IsActive;

        await _context.SaveChangesAsync();

        this.ToastSuccess($"Supplier '{supplier.Name}' updated.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Suppliers/ToggleActive/5 - deactivate rather than delete, since purchase order
    // history references the supplier and must stay intact.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var supplier = await _context.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound();

        supplier.IsActive = !supplier.IsActive;
        await _context.SaveChangesAsync();

        this.ToastSuccess(supplier.IsActive
            ? $"'{supplier.Name}' reactivated."
            : $"'{supplier.Name}' deactivated - existing purchase orders are unaffected.");

        return RedirectToAction(nameof(Index));
    }
}
