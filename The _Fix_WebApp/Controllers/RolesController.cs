using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
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
    /// <summary>Hard ceiling on the total number of roles the system carries at once (built-in
    /// + custom combined). Keeps the permission matrix and the staff-facing role picker from
    /// growing unbounded - delete an unused role to make room for a new one.</summary>
    private const int MaxRoles = 5;

    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public RolesController(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        SignInManager<ApplicationUser> signInManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
        _signInManager = signInManager;
    }

    /// <summary>The signed-in user's cookie carries their permission claims, so after the Administrator role changes the
    /// current admin is re-signed-in straight away and sees the change immediately (everyone else picks it up within
    /// the 5-minute security-stamp check).</summary>
    private async Task RefreshCurrentUserAsync()
    {
        var me = await _userManager.GetUserAsync(User);
        if (me is not null) await _signInManager.RefreshSignInAsync(me);
    }

    // GET: /Roles - the full permission matrix (every role x every permission, edited and
    // saved together). Replaces the old one-row-per-role list; Edit/{id} still exists for
    // any code that links to it, but this is the primary screen now.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new PermissionMatrixViewModel();

        foreach (var role in _roleManager.Roles.OrderBy(r => r.Name))
        {
            var claims = await _roleManager.GetClaimsAsync(role);
            var memberCount = (await _userManager.GetUsersInRoleAsync(role.Name!)).Count;

            model.Roles.Add(new RoleMatrixColumnViewModel
            {
                Id = role.Id,
                Name = role.Name!,
                IsProtected = ProtectedRoles.Contains(role.Name),
                MemberCount = memberCount,
                GrantedPermissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToHashSet()
            });
        }

        ViewBag.MaxRoles = MaxRoles;
        ViewBag.AtRoleCap = model.Roles.Count >= MaxRoles;
        return View(model);
    }

    // POST: /Roles/SaveMatrix - applies every role's checkbox state in one pass. Customer and
    // Administrator columns are locked in the UI (Customer has no staff permissions to grant;
    // Administrator always keeps every permission), and both are re-enforced here too, since
    // a disabled checkbox in the browser is a UI courtesy, not a security boundary.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMatrix(List<string> roleIds, List<string> selections)
    {
        selections ??= new List<string>();
        var changedRoles = new List<string>();

        foreach (var roleId in roleIds.Distinct())
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role is null) continue;

            var desired = selections
                .Where(s => s.StartsWith(roleId + ":", StringComparison.Ordinal))
                .Select(s => s[(roleId.Length + 1)..])
                .ToHashSet();

            // Customer (self-service, not a staff role) always has no staff permissions. The Administrator
            // can now trim their own permissions - EXCEPT the locked ones (Roles & Permissions), which are
            // always forced back on so the admin can never lock themselves out of this screen. Both rules
            // are enforced here regardless of what the browser submitted.
            if (role.Name == "Administrator")
            {
                desired.UnionWith(Permissions.AdministratorLocked);
                desired.IntersectWith(Permissions.All.Keys);
            }
            else if (role.Name == "Customer")
                desired = new HashSet<string>();

            var existingClaims = await _roleManager.GetClaimsAsync(role);
            var existingPermissions = existingClaims.Where(c => c.Type == Permissions.ClaimType).ToList();

            var toRemove = existingPermissions.Where(c => !desired.Contains(c.Value)).ToList();
            var toAdd = desired.Where(p => !existingPermissions.Any(c => c.Value == p)).ToList();

            if (toRemove.Count == 0 && toAdd.Count == 0) continue;

            foreach (var claim in toRemove)
                await _roleManager.RemoveClaimAsync(role, claim);
            foreach (var permission in toAdd)
                await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));

            changedRoles.Add(role.Name!);
        }

        if (changedRoles.Count > 0)
        {
            if (changedRoles.Contains("Administrator")) await RefreshCurrentUserAsync();
            await LogAuditAsync("RolePermissionsUpdated", $"Updated permissions for: {string.Join(", ", changedRoles)}.");
            this.ToastSuccess($"Permissions updated for {changedRoles.Count} role(s).");
        }
        else
        {
            this.ToastSuccess("No changes to save.");
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: /Roles/RestoreAdministrator - the "redo": puts every permission back on the Administrator role in one click.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreAdministrator()
    {
        var role = await _roleManager.FindByNameAsync("Administrator");
        if (role is null) return NotFound();

        var existing = (await _roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        var restored = 0;
        foreach (var permission in Permissions.All.Keys.Where(p => !existing.Contains(p)))
        {
            await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));
            restored++;
        }

        if (restored > 0)
        {
            await RefreshCurrentUserAsync();
            await LogAuditAsync("RolePermissionsUpdated", $"Restored {restored} permission(s) to the Administrator role.");
            this.ToastSuccess($"Administrator permissions restored ({restored} added back).");
        }
        else
        {
            this.ToastSuccess("Administrator already has every permission.");
        }

        return RedirectToAction(nameof(Index));
    }

    // GET: /Roles/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (await _roleManager.Roles.CountAsync() >= MaxRoles)
        {
            this.ToastError($"You've reached the maximum of {MaxRoles} roles. Delete an unused role before creating a new one.");
            return RedirectToAction(nameof(Index));
        }

        return View(new RoleEditViewModel());
    }

    // POST: /Roles/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        if (await _roleManager.Roles.CountAsync() >= MaxRoles)
        {
            ModelState.AddModelError(string.Empty, $"You've reached the maximum of {MaxRoles} roles. Delete an unused role before creating a new one.");
            return View(model);
        }
        if (await _roleManager.RoleExistsAsync(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), $"A role called '{model.Name}' already exists.");
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

        // Administrator can trim their own permissions, but Roles & Permissions is always kept so
        // nobody can lock themselves - or the whole system - out of this screen.
        model.SelectedPermissions ??= new List<string>();
        if (role.Name == "Administrator")
        {
            foreach (var locked in Permissions.AdministratorLocked)
                if (!model.SelectedPermissions.Contains(locked)) model.SelectedPermissions.Add(locked);
        }
        model.SelectedPermissions = model.SelectedPermissions.Where(Permissions.All.ContainsKey).Distinct().ToList();

        var existingClaims = await _roleManager.GetClaimsAsync(role);
        var existingPermissions = existingClaims.Where(c => c.Type == Permissions.ClaimType).ToList();

        foreach (var claim in existingPermissions.Where(c => !model.SelectedPermissions.Contains(c.Value)))
            await _roleManager.RemoveClaimAsync(role, claim);

        foreach (var permission in model.SelectedPermissions.Where(p => !existingPermissions.Any(c => c.Value == p)))
            await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));

        if (role.Name == "Administrator") await RefreshCurrentUserAsync();
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