using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Lets an authorised admin manage the storefront's departments (Women/Men/Kids/Footwear/...)
/// and their subcategory filters, previously only settable via the Program.cs seed block.
/// Departments are soft-deactivated rather than hard-deleted (same convention as Products/
/// Employees) since products can be linked to one - deactivating just hides it from the
/// storefront nav/homepage without breaking any product that references it (Product.DepartmentId
/// is nullable and set to SetNull if a department is ever hard-removed at the DB level, but
/// this screen never does that).
/// </summary>
[Authorize(Policy = Permissions.StorefrontManage)]
public class DepartmentsController : Controller
{
    private readonly ApplicationDbContext _context;

    public DepartmentsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Departments
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var departments = await _context.Departments
            .AsNoTracking()
            .Include(d => d.SubCategories)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync();

        // Product counts shown alongside each department so an admin can see at a glance
        // whether deactivating one would hide products from the storefront.
        var productCounts = await _context.Products
            .Where(p => p.IsActive && p.DepartmentId != null)
            .GroupBy(p => p.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId!.Value, x => x.Count);

        ViewBag.ProductCounts = productCounts;
        return View(departments);
    }

    // GET: /Departments/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var maxOrder = await _context.Departments.AnyAsync() ? await _context.Departments.MaxAsync(d => d.DisplayOrder) : 0;
        return View(new DepartmentViewModel { DisplayOrder = maxOrder + 1 });
    }

    // POST: /Departments/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartmentViewModel model)
    {
        if (await _context.Departments.AnyAsync(d => d.Slug == model.Slug))
            ModelState.AddModelError(nameof(model.Slug), "That slug is already in use by another department.");

        if (!ModelState.IsValid)
            return View(model);

        var department = new Department
        {
            Name = model.Name,
            Slug = model.Slug,
            HeroImageUrl = model.HeroImageUrl,
            HeroHeadline = model.HeroHeadline,
            HeroSubheadline = model.HeroSubheadline,
            TileImageUrl = model.TileImageUrl,
            DisplayOrder = model.DisplayOrder,
            IsActive = model.IsActive
        };

        foreach (var sub in model.SubCategories.Where(s => !s.Remove))
        {
            department.SubCategories.Add(new DepartmentSubCategory
            {
                Name = sub.Name,
                Slug = sub.Slug,
                DisplayOrder = sub.DisplayOrder
            });
        }

        _context.Departments.Add(department);
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{department.Name}' department created.");
        return RedirectToAction(nameof(Index));
    }

    // GET: /Departments/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var department = await _context.Departments
            .Include(d => d.SubCategories.OrderBy(s => s.DisplayOrder))
            .FirstOrDefaultAsync(d => d.DepartmentId == id);

        if (department is null) return NotFound();

        var model = new DepartmentViewModel
        {
            DepartmentId = department.DepartmentId,
            Name = department.Name,
            Slug = department.Slug,
            HeroImageUrl = department.HeroImageUrl,
            HeroHeadline = department.HeroHeadline,
            HeroSubheadline = department.HeroSubheadline,
            TileImageUrl = department.TileImageUrl,
            DisplayOrder = department.DisplayOrder,
            IsActive = department.IsActive,
            SubCategories = department.SubCategories.Select(s => new DepartmentSubCategoryInputViewModel
            {
                DepartmentSubCategoryId = s.DepartmentSubCategoryId,
                Name = s.Name,
                Slug = s.Slug,
                DisplayOrder = s.DisplayOrder
            }).ToList()
        };

        return View(model);
    }

    // POST: /Departments/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, DepartmentViewModel model)
    {
        if (id != model.DepartmentId) return BadRequest();

        if (await _context.Departments.AnyAsync(d => d.Slug == model.Slug && d.DepartmentId != id))
            ModelState.AddModelError(nameof(model.Slug), "That slug is already in use by another department.");

        if (!ModelState.IsValid)
            return View(model);

        var department = await _context.Departments
            .Include(d => d.SubCategories)
            .FirstOrDefaultAsync(d => d.DepartmentId == id);
        if (department is null) return NotFound();

        department.Name = model.Name;
        department.Slug = model.Slug;
        department.HeroImageUrl = model.HeroImageUrl;
        department.HeroHeadline = model.HeroHeadline;
        department.HeroSubheadline = model.HeroSubheadline;
        department.TileImageUrl = model.TileImageUrl;
        department.DisplayOrder = model.DisplayOrder;
        department.IsActive = model.IsActive;

        // Subcategories have no soft-delete flag of their own (they're just a name/slug pair
        // used for filtering, not referenced by a hard FK from Product), so a removed row is
        // deleted outright rather than deactivated - unlike Departments/Products themselves.
        foreach (var row in model.SubCategories)
        {
            if (row.Remove)
            {
                if (row.DepartmentSubCategoryId != 0)
                {
                    var existing = department.SubCategories.FirstOrDefault(s => s.DepartmentSubCategoryId == row.DepartmentSubCategoryId);
                    if (existing is not null) _context.DepartmentSubCategories.Remove(existing);
                }
                continue;
            }

            if (row.DepartmentSubCategoryId == 0)
            {
                department.SubCategories.Add(new DepartmentSubCategory
                {
                    Name = row.Name,
                    Slug = row.Slug,
                    DisplayOrder = row.DisplayOrder
                });
            }
            else
            {
                var existing = department.SubCategories.FirstOrDefault(s => s.DepartmentSubCategoryId == row.DepartmentSubCategoryId);
                if (existing is not null)
                {
                    existing.Name = row.Name;
                    existing.Slug = row.Slug;
                    existing.DisplayOrder = row.DisplayOrder;
                }
            }
        }

        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{department.Name}' department updated.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Departments/ToggleActive/5 - soft "remove"/restore, matching the rest of the app's
    // never-hard-delete convention for anything a Product can reference.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var department = await _context.Departments.FindAsync(id);
        if (department is null) return NotFound();

        department.IsActive = !department.IsActive;
        await _context.SaveChangesAsync();

        this.ToastSuccess(department.IsActive
            ? $"'{department.Name}' is active again and visible on the storefront."
            : $"'{department.Name}' was removed from the storefront (deactivated, not deleted - its products are untouched).");

        return RedirectToAction(nameof(Index));
    }
}
