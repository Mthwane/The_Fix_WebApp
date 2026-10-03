using FashionFix.Web.Controllers;
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.ViewComponents;

/// <summary>
/// The "Start your shift" pop-up shown straight after a staff member logs in. It only appears when ALL of these hold:
/// the login just happened (a one-shot session flag set by AccountController), the person can use the till
/// (pos.use), and they don't already have a shift open. The flag is cleared as soon as it is read, so the pop-up shows
/// once per login - "Not now" dismisses it and it won't nag on every page.
/// </summary>
public class ShiftPromptViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ShiftPromptViewComponent(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var session = HttpContext.Session;
        if (session.GetString(AccountController.ShiftPromptKey) != "1")
            return Content(string.Empty);

        // One-shot: consume the flag whatever happens next.
        session.Remove(AccountController.ShiftPromptKey);

        if (!UserClaimsPrincipal.HasClaim(Permissions.ClaimType, Permissions.PosUse))
            return Content(string.Empty);

        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        if (userId is null) return Content(string.Empty);

        var hasOpenShift = await _context.ShiftSessions.AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (hasOpenShift) return Content(string.Empty);

        var user = await _userManager.GetUserAsync(UserClaimsPrincipal);
        ViewBag.FirstName = (user?.FullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        ViewBag.ReturnUrl = Request.Path + Request.QueryString;
        return View();
    }
}
