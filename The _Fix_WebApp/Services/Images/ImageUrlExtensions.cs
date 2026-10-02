namespace FashionFix.Web.Services.Images;

public static class ImageUrlExtensions
{
    /// <summary>
    /// For Cloudinary-hosted images, returns a URL that delivers an auto-format (WebP/AVIF),
    /// auto-quality copy no wider than <paramref name="width"/> px - this is what keeps pages fast.
    /// Any other URL (local upload, pasted external link, placeholder) is returned unchanged.
    /// </summary>
    public static string Sized(this string? url, int width)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        const string marker = "res.cloudinary.com/";
        const string upload = "/image/upload/";

        if (!url.Contains(marker, StringComparison.OrdinalIgnoreCase)) return url;
        var idx = url.IndexOf(upload, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return url;

        var rest = url[(idx + upload.Length)..];
        // Already has a delivery transformation (we only ever add "f_auto..."), leave it alone.
        if (rest.StartsWith("f_auto", StringComparison.OrdinalIgnoreCase)) return url;

        return $"{url[..(idx + upload.Length)]}f_auto,q_auto,c_limit,w_{width}/{rest}";
    }
}
