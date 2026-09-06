using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Backs the storefront footer "Subscribe" form (see _StorefrontLayout.cshtml), which
/// previously posted nowhere - no backend, no entity, no confirmation. Anonymous by design;
/// most subscribers browsing the storefront footer aren't logged in.
/// </summary>
[AllowAnonymous]
public class NewsletterController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<NewsletterController> _logger;

    public NewsletterController(ApplicationDbContext context, IEmailSender emailSender, ILogger<NewsletterController> logger)
    {
        _context = context;
        _emailSender = emailSender;
        _logger = logger;
    }

    // POST: /Newsletter/Subscribe - posted from the footer form on every storefront page.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(string email, string? returnUrl)
    {
        var redirectTarget = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action("Index", "Home")!;

        if (string.IsNullOrWhiteSpace(email) || !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            this.ToastError("That doesn't look like a valid email address - please try again.");
            return Redirect(redirectTarget);
        }

        email = email.Trim().ToLowerInvariant();

        var existing = await _context.EmailSubscribers.FirstOrDefaultAsync(s => s.Email == email);
        if (existing is not null)
        {
            if (existing.IsActive)
            {
                this.ToastSuccess("You're already on the list - no need to sign up twice!");
                return Redirect(redirectTarget);
            }

            // Re-subscribing after a past unsubscribe reactivates the same row.
            existing.IsActive = true;
            existing.DateSubscribed = DateTime.UtcNow;
        }
        else
        {
            _context.EmailSubscribers.Add(new EmailSubscriber { Email = email });
        }

        await _context.SaveChangesAsync();

        // Best-effort - a failed confirmation email should never block the subscription itself
        // (IEmailSender never throws on delivery failure; see its own doc comment).
        try
        {
            await _emailSender.SendAsync(email, "You're subscribed to the Conservatory Dispatch",
                "<h2>You're in.</h2><p>Thanks for subscribing to the Conservatory Dispatch - " +
                "new arrivals, seasonal capsules, and the odd behind-the-scenes note, straight to your inbox.</p>" +
                "<p>If this wasn't you, just ignore this email - you won't be added again.</p>");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send newsletter confirmation email to {Email}.", email);
        }

        this.ToastSuccess("You're subscribed! Check your inbox for a confirmation email.");
        return Redirect(redirectTarget);
    }
}
