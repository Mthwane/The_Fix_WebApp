using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services;

public class AuthResult
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAt { get; set; }
}

public interface ITokenService
{
    /// <summary>
    /// Builds a signed JWT for the given user. Role claims are looked up fresh from
    /// UserManager at call time - nothing about "who has which role" is hardcoded here.
    /// </summary>
    Task<(string token, DateTime expiresAt)> CreateAccessTokenAsync(ApplicationUser user);

    /// <summary>Issues a new refresh token, stores its hash, and returns the raw value (only ever returned once).</summary>
    Task<(string token, DateTime expiresAt)> CreateRefreshTokenAsync(ApplicationUser user, string? createdByIp);

    /// <summary>
    /// Validates a presented refresh token against the stored hash, and if valid, rotates it
    /// (revokes the old one, issues a new one) to limit the blast radius of a stolen token.
    /// Returns null if the token is invalid, expired, or already revoked/reused.
    /// </summary>
    Task<AuthResult?> RefreshAsync(string presentedRefreshToken, string? requestIp);

    /// <summary>Revokes a specific refresh token (used on logout).</summary>
    Task RevokeAsync(string presentedRefreshToken);

    /// <summary>Revokes every active refresh token for a user (used when an account is deactivated).</summary>
    Task RevokeAllForUserAsync(string userId);
}
