using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>Staff CRUD for the public FAQ page's content - grouped with other storefront
/// content management under StorefrontManage, same as Departments.</summary>
[Authorize(Policy = Permissions.StorefrontManage)]
public class FaqsController : Controller
{
    private readonly ApplicationDbContext _context;

    public FaqsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Faqs
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var faqs = await _context.Faqs
            .OrderBy(f => f.Category)
            .ThenBy(f => f.DisplayOrder)
            .ToListAsync();
        return View(faqs);
    }

    // GET: /Faqs/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewBag.ExistingCategories = await _context.Faqs.Select(f => f.Category).Distinct().OrderBy(c => c).ToListAsync();
        return View(new Faq());
    }

    // POST: /Faqs/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Faq model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ExistingCategories = await _context.Faqs.Select(f => f.Category).Distinct().OrderBy(c => c).ToListAsync();
            return View(model);
        }

        _context.Faqs.Add(model);
        await _context.SaveChangesAsync();
        this.ToastSuccess("FAQ added.");
        return RedirectToAction(nameof(Index));
    }

    // GET: /Faqs/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var faq = await _context.Faqs.FindAsync(id);
        if (faq is null) return NotFound();
        return View(faq);
    }

    // POST: /Faqs/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Faq model)
    {
        if (id != model.FaqId) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var faq = await _context.Faqs.FindAsync(id);
        if (faq is null) return NotFound();

        faq.Question = model.Question;
        faq.Answer = model.Answer;
        faq.Category = model.Category;
        faq.DisplayOrder = model.DisplayOrder;
        faq.IsActive = model.IsActive;
        await _context.SaveChangesAsync();

        this.ToastSuccess("FAQ updated.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Faqs/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var faq = await _context.Faqs.FindAsync(id);
        if (faq is null) return NotFound();

        _context.Faqs.Remove(faq);
        await _context.SaveChangesAsync();
        this.ToastSuccess("FAQ deleted.");
        return RedirectToAction(nameof(Index));
    }
}
