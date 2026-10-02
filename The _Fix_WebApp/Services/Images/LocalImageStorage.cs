using Microsoft.AspNetCore.Http;

namespace FashionFix.Web.Services.Images;

/// <summary>
/// Fallback used when Cloudinary isn't configured (typical local development). Saves under
/// wwwroot/uploads. Do NOT rely on this in Azure App Service - local disk there isn't a durable,
/// scalable store; configure Cloudinary for production.
/// </summary>
public class LocalImageStorage : IImageStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalImageStorage> _logger;

    public LocalImageStorage(IWebHostEnvironment env, ILogger<LocalImageStorage> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<ImageUploadResult> UploadAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        var error = await ImageFileValidator.ValidateAsync(file, ct);
        if (error is not null) return ImageUploadResult.Fail(error);

        var safeFolder = new string(folder.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        if (safeFolder.Length == 0) safeFolder = "misc";

        try
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var dir = Path.Combine(webRoot, "uploads", safeFolder);
            Directory.CreateDirectory(dir);

            await using (var fs = new FileStream(Path.Combine(dir, fileName), FileMode.CreateNew))
            {
                await file.CopyToAsync(fs, ct);
            }

            return ImageUploadResult.Ok($"/uploads/{safeFolder}/{fileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local image save failed");
            return ImageUploadResult.Fail("Couldn't save the image (" + ex.GetType().Name + "). Check the server log.");
        }
    }

    public Task DeleteAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/uploads/", StringComparison.Ordinal)) return Task.CompletedTask;

        try
        {
            var relative = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, relative));
            var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads")) + Path.DirectorySeparatorChar;
            if (full.StartsWith(uploadsRoot, StringComparison.Ordinal) && File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local image delete failed for {Url}", url);
        }
        return Task.CompletedTask;
    }
}
