using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class EmployeesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EmployeesController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // GET: /Employees - lists staff accounts (US-15).
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Staff = any user NOT solely in the Customer role.
        var customerRoleUsers = await _userManager.GetUsersInRoleAsync("Customer");
        var customerIds = customerRoleUsers.Select(u => u.Id).ToHashSet();

        var employees = await _context.Users
            .Where(u => !customerIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .ToListAsync();

        return View(employees);
    }

    // GET: /Employees/Create
    [HttpGet]
    public IActionResult CreateEmployee() => View(new EmployeeViewModel());

    // POST: /Employees/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEmployee(EmployeeViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var existingByUsername = await _userManager.FindByNameAsync(model.Username);
        if (existingByUsername is not null)
        {
            ModelState.AddModelError(nameof(model.Username), "That username is already taken.");
            return View(model);
        }

        var existingByEmail = await _userManager.FindByEmailAsync(model.Email);
        if (existingByEmail is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Username,
            Email = model.Email,
            FullName = model.FullName,
            JobPosition = model.JobPosition,
            EmploymentStatus = "Active",
            DateHired = DateTime.UtcNow,
            IsActive = true,
            EmailConfirmed = true,
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, model.Role);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeCreated",
            Details = $"Created {model.Role} account for '{user.UserName}' ({model.JobPosition}).",
        });
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // POST: /Employees/AssignRole - role/permission assignment (US-16).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRole(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var previousRoles = string.Join(", ", currentRoles);

        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, role);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "RoleAssigned",
            Details = $"Changed '{user.UserName}' role from [{previousRoles}] to '{role}'.",
        });
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // GET: /Employees/AuditLogs - admin-only audit trail (NFR-11).
    [HttpGet]
    public async Task<IActionResult> AuditLogs()
    {
        var logs = await _context.AuditLogs
            .Include(a => a.User)
            .OrderByDescending(a => a.Timestamp)
            .Take(200)
            .ToListAsync();

        return View(logs);
    }
}
