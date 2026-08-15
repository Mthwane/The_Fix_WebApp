using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Handles authentication for every entity in the system (US-10, plus the login/verification
/// requirements in the "User Authentication" functional requirements). Customers self-register
/// and sign in through the branded landing page; staff (Administrator/Manager/Employee/Owner)
/// sign in through the dedicated Employee Login screen so the two audiences never get confused.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private static readonly string[] StaffRoles = { "Administrator", "Manager", "Employee", "Owner" };

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    // GET: /Account/Login - the customer login form itself lives on Home/Index (the branded
    // landing page), so a direct GET here just lands you back on that page.
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard", "Home");

        return RedirectToAction("Index", "Home", new { returnUrl });
    }

    // POST: /Account/Login - handles BOTH the customer login form (Home/Index) and the
    // staff login form (Account/EmployeeLogin); model.IsEmployeeLogin tells us which.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var viewName = model.IsEmployeeLogin ? "EmployeeLogin" : "~/Views/Home/Index.cshtml";

        if (!ModelState.IsValid)
            return View(viewName, model);

        var user = await _userManager.FindByNameAsync(model.Username);
        if (user is not null && !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated. Contact an administrator.");
            return View(viewName, model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Username, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded && user is not null)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var isStaff = roles.Any(r => StaffRoles.Contains(r));

            // Keep the two portals separate: staff must use the Employee Login screen,
            // customers must use the main storefront login.
            if (model.IsEmployeeLogin && !isStaff)
            {
                await _signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "This account isn't a staff account. Please use the customer login instead.");
                return View(viewName, model);
            }

            if (!model.IsEmployeeLogin && isStaff && !roles.Contains("Customer"))
            {
                await _signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "Staff accounts must sign in from the Employee Login screen.");
                return View(viewName, model);
            }

            await LogAuditAsync(user.Id, "Login", $"'{user.UserName}' signed in ({string.Join(", ", roles)}).");
            return RedirectAfterLogin(model.ReturnUrl, roles);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked due to repeated failed attempts. Try again later.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
        }

        return View(viewName, model);
    }

    // GET: /Account/EmployeeLogin - dedicated staff sign-in screen (Administrator, Manager,
    // Employee, Owner). Kept visually distinct from the customer storefront login.
    [HttpGet]
    public IActionResult EmployeeLogin(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard", "Home");

        return View(new LoginViewModel { IsEmployeeLogin = true, ReturnUrl = returnUrl });
    }

    // POST: /Account/Logout
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is not null)
            await LogAuditAsync(userId, "Logout", "User signed out.");

        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // GET: /Account/Register - customer self-registration only. Staff accounts are created
    // by an Administrator via /Employees/CreateEmployee so role assignment stays controlled.
    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    // POST: /Account/Register
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
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

        await _userManager.AddToRoleAsync(user, "Customer");
        await LogAuditAsync(user.Id, "CustomerRegistered", $"New customer account '{user.UserName}' created.");
        await _signInManager.SignInAsync(user, isPersistent: false);

        return RedirectToAction("Orders", "Customer");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // GET: /Account/ChangePassword - available to every signed-in user (staff or customer).
    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    // POST: /Account/ChangePassword
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        // Re-sign the user in so their auth cookie reflects the new security stamp
        // (ChangePasswordAsync rotates it) instead of getting logged out unexpectedly.
        await _signInManager.RefreshSignInAsync(user);

        await LogAuditAsync(user.Id, "PasswordChanged", $"'{user.UserName}' changed their password.");

        TempData["PasswordChanged"] = true;
        return RedirectToAction(nameof(ChangePassword));
    }

    private IActionResult RedirectAfterLogin(string? returnUrl, IList<string> roles)
    {
        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        // Customers land on their own account area; every staff role lands on the dashboard.
        if (roles.Contains("Customer") && !roles.Any(r => StaffRoles.Contains(r)))
            return RedirectToAction("Orders", "Customer");

        return RedirectToAction("Dashboard", "Home");
    }

    private async Task LogAuditAsync(string userId, string action, string? details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            Details = details
        });
        await _context.SaveChangesAsync();
    }
}
