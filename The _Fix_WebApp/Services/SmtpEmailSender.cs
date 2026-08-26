using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace FashionFix.Web.Services;

/// <summary>
/// Configuration for the "Email" section in appsettings.json / environment variables / user-secrets.
/// Deliberately just plain SMTP - no paid third-party API required. Any free SMTP relay works:
///   - Gmail: smtp.gmail.com, port 587, an "App Password" (not your normal password) - free
///   - Outlook/Hotmail: smtp-mail.outlook.com, port 587 - free
///   - Zoho Mail free tier: smtp.zoho.com, port 587 - free
/// Leave Host empty to disable email entirely (the app runs fine without it - notifications
/// are best-effort and every call site treats a send failure as non-fatal).
/// </summary>
public class EmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = "no-reply@fashionfix.local";
    public string FromName { get; set; } = "Fashion Fix";
}

public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            // Email isn't configured - log it and move on rather than breaking the caller's flow.
            _logger.LogInformation("Email not configured - skipped sending '{Subject}' to {To}.", subject, toEmail);
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                Credentials = new NetworkCredential(_options.Username, _options.Password)
            };

            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            // Never let a notification failure break the caller (a checkout, a sale, etc).
            _logger.LogError(ex, "Failed to send email '{Subject}' to {To}.", subject, toEmail);
        }
    }
}
