using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Server-side record of an issued refresh token, so tokens can be revoked/rotated instead of
/// being valid until they simply expire. We never store the raw token - only its SHA-256 hash -
/// so a database leak alone can't be used to mint new access tokens.
/// </summary>
public class RefreshToken
{
    [Key]
    public int RefreshTokenId { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one, when rotated. Lets us detect reuse of a stale token.</summary>
    public string? ReplacedByTokenHash { get; set; }

    [MaxLength(64)]
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
