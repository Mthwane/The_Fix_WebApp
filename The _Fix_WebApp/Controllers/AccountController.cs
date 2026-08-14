using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FashionFix.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    // GET: /Account/Login - the login form itself lives on Home/Index (the branded landing page),
    // so a direct GET here just lands you back on that page.
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return RedirectToAction("Index", "Home", new { returnUrl });
    }

    // POST: /Account/Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Views/Home/Index.cshtml", model);

        // TODO: swap FindByNameAsync for a lookup that also allows email login if required.
        var result = await _signInManager.PasswordSignInAsync(
            model.Username, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // TODO: write an AuditLog entry here ("Login") per NFR-11 Audit and Logging.
            return RedirectToLocal(model.ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked. Try again later.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
        }

        return View("~/Views/Home/Index.cshtml", model);
    }

    // GET: /Account/EmployeeLogin - placeholder staff-only login (US matches customer login shape).
    [HttpGet]
    public IActionResult EmployeeLogin() => View();

    // POST: /Account/Logout
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // GET: /Account/Register
    [HttpGet]
    public IActionResult Register() => View();

    // POST: /Account/Register
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(/* RegisterViewModel model */)
    {
        // TODO: create a RegisterViewModel and wire up _userManager.CreateAsync,
        // then assign the "Customer" role by default.
        await Task.CompletedTask;
        return View();
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        return RedirectToAction("Dashboard", "Home");
    }
}
