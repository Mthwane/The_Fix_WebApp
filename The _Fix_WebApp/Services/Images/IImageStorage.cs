using Microsoft.AspNetCore.Http;

namespace FashionFix.Web.Services.Images;

/// <summary>Outcome of an upload. Url is the public URL to store in the existing *ImageUrl columns.</summary>
public record ImageUploadResult(bool Success, string? Url, string? Error)
{
    public static ImageUploadResult Ok(string url) => new(true, url, null);
    public static ImageUploadResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Where uploaded images live. Controllers only talk to this interface, so storage can be
/// swapped (Cloudinary, local disk, Azure Blob later) without touching them. Stored values
/// are plain URLs in the existing ImageUrl columns - no schema change.
/// </summary>
public interface IImageStorage
{
    /// <summary>Validates and stores the file. folder is a logical bucket such as "products" or "departments".</summary>
    Task<ImageUploadResult> UploadAsync(IFormFile file, string folder, CancellationToken ct = default);

    /// <summary>Best-effort delete of an image previously returned by UploadAsync. Ignores URLs it doesn't own (pasted links) and never throws.</summary>
    Task DeleteAsync(string? url, CancellationToken ct = default);
}
