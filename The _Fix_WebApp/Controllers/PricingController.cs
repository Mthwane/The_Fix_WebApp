using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

// Gated on ProductsManage rather than a new permission constant - pricing is a facet of
// catalogue management, not a distinct concern, and every role that can already touch
// products (Admin, Manager) should be able to touch what those products auto-price to.
[Authorize(Policy = Permissions.ProductsManage)]
public class PricingController : Controller
{
    private readonly ApplicationDbContext _context;

    public PricingController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Pricing
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await _context.PricingSettings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1)
            ?? new PricingSettings();

        var rules = await _context.CategoryPricingRules.AsNoTracking().ToListAsync();

        var model = new PricingSettingsViewModel
        {
            DefaultMarkupPercentage = settings.DefaultMarkupPercentage,
            CategoryRules = ProductViewModel.Categories.Select(cat => new CategoryMarkupRow
            {
                Category = cat,
                Rule = rules.FirstOrDefault(r => r.Category == cat)
            }).ToList()
        };

        return View(model);
    }

    // POST: /Pricing/SaveDefault - the store-wide fallback markup.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDefault(decimal markupPercentage)
    {
        if (markupPercentage < 0 || markupPercentage > 1000)
        {
            this.ToastError("Markup must be between 0% and 1000%.");
            return RedirectToAction(nameof(Index));
        }

        var settings = await _context.PricingSettings.FirstOrDefaultAsync(p => p.Id == 1);
        if (settings is null)
        {
            settings = new PricingSettings { Id = 1 };
            _context.PricingSettings.Add(settings);
        }

        settings.DefaultMarkupPercentage = markupPercentage;
        settings.DateUpdated = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        this.ToastSuccess($"Default markup set to {markupPercentage}%. New products (and any auto-calculated price without a category-specific rule) will use this from now on - existing products are unaffected until they're next saved.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Pricing/SaveCategoryRule - upsert a per-category override.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCategoryRule(string category, decimal markupPercentage)
    {
        if (!ProductViewModel.Categories.Contains(category))
        {
            this.ToastError("Unrecognised category.");
            return RedirectToAction(nameof(Index));
        }

        if (markupPercentage < 0 || markupPercentage > 1000)
        {
            this.ToastError("Markup must be between 0% and 1000%.");
            return RedirectToAction(nameof(Index));
        }

        var rule = await _context.CategoryPricingRules.FirstOrDefaultAsync(r => r.Category == category);
        if (rule is null)
        {
            rule = new CategoryPricingRule { Category = category };
            _context.CategoryPricingRules.Add(rule);
        }

        rule.MarkupPercentage = markupPercentage;
        rule.DateUpdated = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        this.ToastSuccess($"{category} now marks up at {markupPercentage}%.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Pricing/DeleteCategoryRule/5 - removes the override, the category falls back to the store default.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategoryRule(int id)
    {
        var rule = await _context.CategoryPricingRules.FindAsync(id);
        if (rule is null) return NotFound();

        var category = rule.Category;
        _context.CategoryPricingRules.Remove(rule);
        await _context.SaveChangesAsync();

        this.ToastSuccess($"{category} now follows the store default markup again.");
        return RedirectToAction(nameof(Index));
    }
}
