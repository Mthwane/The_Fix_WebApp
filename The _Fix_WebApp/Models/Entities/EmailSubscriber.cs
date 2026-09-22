using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// An email address captured from the storefront footer "Subscribe" form. That form posted
/// nowhere before this - no backend, no entity, submitting it silently did nothing. Kept
/// separate from ApplicationUser since most subscribers are anonymous visitors, not accounts.
/// </summary>
public class EmailSubscriber
{
    [Key]
    public int EmailSubscriberId { get; set; }

    [Required, MaxLength(256), EmailAddress]
    public string Email { get; set; } = string.Empty;

    public DateTime DateSubscribed { get; set; } = DateTime.UtcNow;

    /// <summary>False after an unsubscribe - kept rather than deleted so re-subscribing doesn't
    /// send a duplicate "welcome" email and history isn't lost.</summary>
    public bool IsActive { get; set; } = true;
}
