namespace FashionFix.Web.Services.Images;

/// <summary>Bound from the "Cloudinary" config section. Keep ApiSecret in user-secrets / App Service settings, never in appsettings.json.</summary>
public class CloudinarySettings
{
    public string? CloudName { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }

    /// <summary>Top-level Cloudinary folder, so dev and prod can share one account without mixing files.</summary>
    public string Folder { get; set; } = "fashionfix";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);
}
