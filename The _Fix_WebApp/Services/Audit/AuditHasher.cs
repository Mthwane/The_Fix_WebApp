using System.Security.Cryptography;
using System.Text;
using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services.Audit;

public static class AuditHasher
{
    /// <summary>SHA-256 over the row's fields plus the previous row's hash. Timestamp ticks are used (not formatted text) so a round-trip through SQL Server can't change the result.</summary>
    public static string Compute(AuditLog log, string? previousHash)
    {
        var payload = string.Join('\n',
            previousHash ?? string.Empty,
            log.Timestamp.Ticks.ToString(),
            log.UserId ?? string.Empty,
            log.Action,
            log.Details ?? string.Empty,
            log.IpAddress ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
