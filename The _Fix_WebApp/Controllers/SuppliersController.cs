using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>Records supplier details (name, contact, lead time) ahead of raising purchase orders (US-20).</summary>
[Authorize(Roles = "Administrator,Manager,Owner")]
public class SuppliersController : Controller
{
    private readonly ApplicationDbContext _context;

    public SuppliersController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Suppliers
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var suppliers = await _context.Suppliers.OrderBy(s => s.Name).ToListAsync();
        return View(suppliers);
    }

    // GET: /Suppliers/Create
    [HttpGet]
    public IActionResult Create() => View(new SupplierViewModel());

    // POST: /Suppliers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SupplierViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        _context.Suppliers.Add(new Supplier
        {
            Name = model.Name,
            ContactEmail = model.ContactEmail,
            ContactPhone = model.ContactPhone,
            LeadTimeDays = model.LeadTimeDays
        });
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
