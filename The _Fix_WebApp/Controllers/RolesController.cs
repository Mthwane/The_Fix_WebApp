using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Lets an Administrator create/edit roles and choose exactly which permissions each role
/// grants (US-16: assign user roles and permissions). Roles are pure data here - creating a
/// new role, or changing what an existing one can do, takes effect immediately with no code
/// changes or redeploy, because every [Authorize] check in the app is against a permission
/// policy (see Security/Permissions.cs), never a role name.
/// </summary>
[Authorize(Policy = Permissions.RolesManage)]
public class RolesController : Controller
{
    private static readonly string[] ProtectedRoles = { "Administrator", "Customer" };

    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public RolesController(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
    }

    // GET: /Roles
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var roles = new List<RoleListItemViewModel>();

        foreach (var role in _roleManager.Roles.OrderBy(r => r.Name))
        {
            var claims = await _roleManager.GetClaimsAsync(role);
            var memberCount = (await _userManager.GetUsersInRoleAsync(role.Name!)).Count;

            roles.Add(new RoleListItemViewModel
            {
                Id = role.Id,
                Name = role.Name!,
                PermissionCount = claims.Count(c => c.Type == Permissions.ClaimType),
                MemberCount = memberCount,
                IsProtected = ProtectedRoles.Contains(role.Name)
            });
        }

        return View(roles);
    }

    // GET: /Roles/Create
    [HttpGet]
    public IActionResult Create() => View(new RoleEditViewModel());

    // POST: /Roles/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        if (await _roleManager.RoleExistsAsync(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "A role with that name already exists.");
            return View(model);
        }

        var role = new IdentityRole(model.Name);
        var createResult = await _roleManager.CreateAsync(role);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        foreach (var permission in model.SelectedPermissions)
            await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));

        await LogAuditAsync("RoleCreated", $"Created role '{role.Name}' with {model.SelectedPermissions.Count} permission(s).");
        this.ToastSuccess($"Role '{role.Name}' was created.");

        return RedirectToAction(nameof(Index));
    }

    // GET: /Roles/Edit/{id}
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role is null) return NotFound();

        var claims = await _roleManager.GetClaimsAsync(role);

        var model = new RoleEditViewModel
        {
            Id = role.Id,
            Name = role.Name!,
            IsProtected = ProtectedRoles.Contains(role.Name),
            SelectedPermissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList()
        };

        return View(model);
    }

    // POST: /Roles/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, RoleEditViewModel model)
    {
        if (id != model.Id) return BadRequest();

        var role = await _roleManager.FindByIdAsync(id);
        if (role is null) return NotFound();

        // The Administrator role can have any other permission removed, but RolesManage
        // itself can never be unchecked - that's the one permission that guarantees an
        // Administrator can always get back into this screen and grant permissions back,
        // even after removing everything else from the role.
        if (role.Name == "Administrator" && !model.SelectedPermissions.Contains(Permissions.RolesManage))
        {
            model.SelectedPermissions.Add(Permissions.RolesManage);
        }

        var existingClaims = await _roleManager.GetClaimsAsync(role);
        var existingPermissions = existingClaims.Where(c => c.Type == Permissions.ClaimType).ToList();

        foreach (var claim in existingPermissions.Where(c => !model.SelectedPermissions.Contains(c.Value)))
            await _roleManager.RemoveClaimAsync(role, claim);

        foreach (var permission in model.SelectedPermissions.Where(p => !existingPermissions.Any(c => c.Value == p)))
            await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));

        await LogAuditAsync("RolePermissionsUpdated", $"Updated permissions for role '{role.Name}' ({model.SelectedPermissions.Count} permission(s)).");
        this.ToastSuccess($"Permissions for '{role.Name}' were updated.");

        return RedirectToAction(nameof(Index));
    }

    // POST: /Roles/Delete/{id} - blocked for protected roles or roles still in use.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role is null) return NotFound();

        if (ProtectedRoles.Contains(role.Name))
        {
            this.ToastError($"'{role.Name}' is a built-in role and can't be deleted.");
            return RedirectToAction(nameof(Index));
        }

        var members = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (members.Any())
        {
            this.ToastError($"Can't delete '{role.Name}' - {members.Count} user(s) are still assigned to it. Reassign them first.");
            return RedirectToAction(nameof(Index));
        }

        await _roleManager.DeleteAsync(role);
        await LogAuditAsync("RoleDeleted", $"Deleted role '{role.Name}'.");
        this.ToastSuccess($"Role '{role.Name}' was deleted.");

        return RedirectToAction(nameof(Index));
    }

    private async Task LogAuditAsync(string action, string details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = action,
            Details = details
        });
        await _context.SaveChangesAsync();
    }
}