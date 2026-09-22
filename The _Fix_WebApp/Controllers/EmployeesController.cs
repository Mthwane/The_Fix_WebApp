using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

[Authorize]
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
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Index()
    {

        // Staff = any user NOT solely in the Customer role. Previously this called
        // GetRolesAsync per user (twice - once to filter, once to build the label), which is
        // 2N+1 round trips for N users. One joined query gets every user's roles at once.
        var allUsers = await _context.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync();

        var rolesByUserId = (await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, RoleName = r.Name }
            ).ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName ?? string.Empty).ToList());

        var employees = allUsers
            .Where(u => rolesByUserId.TryGetValue(u.Id, out var roles) && roles.Any(r => r != "Customer"))
            .ToList();

        var roleLookup = employees.ToDictionary(
            e => e.Id,
            e => string.Join(", ", rolesByUserId.TryGetValue(e.Id, out var roles) ? roles : new List<string>()));

        ViewBag.Roles = roleLookup;
        ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();

        return View(employees);
    }

    /// <summary>All roles except "Customer" - customers self-register and are never assigned via this screen.</summary>
    private async Task<List<string>> GetAssignableRoleNamesAsync()
    {
        return await Task.FromResult(_roleManager.Roles
            .Where(r => r.Name != "Customer")
            .Select(r => r.Name!)
            .OrderBy(n => n)
            .ToList());
    }

    // GET: /Employees/Create
    [HttpGet]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> CreateEmployee()
    {
        ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
        return View(new EmployeeViewModel());
    }

    // POST: /Employees/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> CreateEmployee(EmployeeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            return View(model);
        }

        var existingByUsername = await _userManager.FindByNameAsync(model.Username);
        if (existingByUsername is not null)
        {
            ModelState.AddModelError(nameof(model.Username), "That username is already taken.");
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            return View(model);
        }

        var existingByEmail = await _userManager.FindByEmailAsync(model.Email);
        if (existingByEmail is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
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
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
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

        this.ToastSuccess($"'{user.UserName}' was created as a {model.Role}.");
        return RedirectToAction(nameof(Index));
    }
   

    // POST: /Employees/AssignRole - role/permission assignment (US-16).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> AssignRole(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var previousRoles = string.Join(", ", currentRoles);

        var rolesToRemove = currentRoles.Where(r => r != "Customer").ToList();
        await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
        await _userManager.AddToRoleAsync(user, role);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "RoleAssigned",
            Details = $"Changed '{user.UserName}' role from [{previousRoles}] to '{role}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' is now a {role}.");
        return RedirectToAction(nameof(Index));
    }

    // GET: /Employees/Edit/{id} - update staff profile details (US-15).
    [HttpGet]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);

        var model = new EmployeeEditViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            JobPosition = user.JobPosition,
            EmploymentStatus = user.EmploymentStatus ?? "Active",
            Role = roles.FirstOrDefault(r => r != "Customer") ?? roles.FirstOrDefault() ?? string.Empty,
            IsActive = user.IsActive
        };

        ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
        return View(model);
    }
    // POST: /Employees/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Edit(string id, EmployeeEditViewModel model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing is not null && existing.Id != user.Id)
            {
                ModelState.AddModelError(nameof(model.Email), "That email is already registered to another account.");
                ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
                return View(model);
            }
            await _userManager.SetEmailAsync(user, model.Email);
        }

        user.FullName = model.FullName;
        user.JobPosition = model.JobPosition;
        user.EmploymentStatus = model.EmploymentStatus;
        user.IsActive = model.IsActive;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.Role))
        {
            var currentRoles = (await _userManager.GetRolesAsync(user)).Where(r => r != "Customer").ToList();
            if (!currentRoles.Contains(model.Role))
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, model.Role);
            }
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeUpdated",
            Details = $"Updated profile for '{user.UserName}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' was updated.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Employees/Deactivate/{id} - soft-deactivate a staff account (never a hard delete,
    // consistent with Product Control's "deactivate without permanently deleting" rule).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = false;
        user.EmploymentStatus = "Terminated";
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeDeactivated",
            Details = $"Deactivated staff account '{user.UserName}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' was deactivated.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Employees/Reactivate/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Reactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = true;
        user.EmploymentStatus = "Active";
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeReactivated",
            Details = $"Reactivated staff account '{user.UserName}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' was reactivated.");
        return RedirectToAction(nameof(Index));
    }


    // GET: /Employees/AuditLogs - admin-only audit trail (NFR-11).
    [Authorize(Policy = Permissions.AuditLogsView)]
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
