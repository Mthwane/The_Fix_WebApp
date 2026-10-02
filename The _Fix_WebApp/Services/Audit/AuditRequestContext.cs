namespace FashionFix.Web.Services.Audit;

/// <summary>
/// Carries the current request's client IP down to the DbContext so every audit row can record
/// it without touching the ~35 places that create AuditLog entries. Set once per request by a
/// small middleware in Program.cs; null in background jobs.
/// </summary>
public static class AuditRequestContext
{
    private static readonly AsyncLocal<string?> _ip = new();

    public static string? IpAddress
    {
        get => _ip.Value;
        set => _ip.Value = value;
    }
}
