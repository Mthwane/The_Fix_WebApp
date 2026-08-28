namespace FashionFix.Web.Services;

public interface IEmailSender
{
    /// <summary>
    /// Sends an email. Implementations should never throw on delivery failure - callers
    /// treat email as best-effort (a failed notification must never block a checkout or
    /// a sale), so failures are logged instead.
    /// </summary>
    Task SendAsync(string toEmail, string subject, string htmlBody);
}
