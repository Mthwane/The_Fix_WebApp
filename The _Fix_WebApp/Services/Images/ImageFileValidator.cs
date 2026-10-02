using Microsoft.AspNetCore.Http;

namespace FashionFix.Web.Services.Images;

/// <summary>Shared upload checks: size, extension, and the file's real header bytes (a renamed .exe is rejected).</summary>
public static class ImageFileValidator
{
    public const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    /// <summary>Returns null if the file is acceptable, otherwise a message safe to show staff.</summary>
    public static async Task<string?> ValidateAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return "No file was selected.";
        if (file.Length > MaxBytes) return "That image is larger than 5 MB. Please resize it and try again.";

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return "Only JPG, PNG, WebP or GIF images are allowed.";

        var header = new byte[12];
        int read;
        await using (var s = file.OpenReadStream())
        {
            read = await s.ReadAsync(header.AsMemory(0, header.Length), ct);
        }
        if (read < 4) return "That file doesn't look like an image.";

        bool jpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        bool png = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        bool gif = header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38;
        bool webp = read >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                    && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;

        return (jpeg || png || gif || webp) ? null : "That file doesn't look like a real image.";
    }
}
