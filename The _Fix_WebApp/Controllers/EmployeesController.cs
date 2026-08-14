using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
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
    public IActionResult CreateEmployee() => View();

    // POST: /Employees/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEmployee(/* EmployeeViewModel model */)
    {
        // TODO: build an EmployeeViewModel (FullName, Username, Email, JobPosition, Role),
        // call _userManager.CreateAsync, then _userManager.AddToRoleAsync.
        await Task.CompletedTask;
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
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, role);

        // TODO: write an AuditLog entry ("RoleAssigned").
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
