using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FashionFix.Web.Services.Images;

/// <summary>
/// Signed uploads straight to Cloudinary's REST API (no SDK dependency). The API secret never
/// leaves the server. Images are capped at 2000px on upload so originals stay small; page-size
/// variants are produced at delivery time by ImageUrlExtensions.Sized.
/// </summary>
public class CloudinaryImageStorage : IImageStorage
{
    private const string UploadTransformation = "c_limit,w_2000,h_2000";

    private readonly IHttpClientFactory _httpFactory;
    private readonly CloudinarySettings _settings;
    private readonly ILogger<CloudinaryImageStorage> _logger;

    public CloudinaryImageStorage(IHttpClientFactory httpFactory, IOptions<CloudinarySettings> settings, ILogger<CloudinaryImageStorage> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ImageUploadResult> UploadAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        var error = await ImageFileValidator.ValidateAsync(file, ct);
        if (error is not null) return ImageUploadResult.Fail(error);

        var fullFolder = $"{_settings.Folder.Trim('/')}/{folder.Trim('/')}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // Signed params, alphabetical. file / api_key / cloud_name / resource_type are not signed.
        var signature = Sign($"folder={fullFolder}&timestamp={timestamp}&transformation={UploadTransformation}");

        try
        {
            using var form = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            var fileContent = new StreamContent(stream);
            form.Add(fileContent, "file", Path.GetFileName(file.FileName));
            form.Add(new StringContent(_settings.ApiKey!), "api_key");
            form.Add(new StringContent(timestamp), "timestamp");
            form.Add(new StringContent(fullFolder), "folder");
            form.Add(new StringContent(UploadTransformation), "transformation");
            form.Add(new StringContent(signature), "signature");

            var http = _httpFactory.CreateClient("cloudinary");
            using var response = await http.PostAsync($"{_settings.CloudName}/image/upload", form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cloudinary upload failed ({Status}): {Body}", (int)response.StatusCode, body);
                return ImageUploadResult.Fail("The image service rejected the upload. Please try again.");
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("secure_url", out var url) && !string.IsNullOrWhiteSpace(url.GetString()))
                return ImageUploadResult.Ok(url.GetString()!);

            _logger.LogWarning("Cloudinary upload returned no secure_url: {Body}", body);
            return ImageUploadResult.Fail("The image service returned an unexpected response.");
        }
        catch (Exception ex)
        {
            // Never let an image problem take the page down; the real cause is in the server log.
            _logger.LogError(ex, "Cloudinary upload threw");
            return ImageUploadResult.Fail("Couldn't upload to the image service (" + ex.GetType().Name + "). Check the server log.");
        }
    }

    public async Task DeleteAsync(string? url, CancellationToken ct = default)
    {
        var publicId = TryGetPublicId(url);
        if (publicId is null) return;

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signature = Sign($"public_id={publicId}&timestamp={timestamp}");

            using var form = new MultipartFormDataContent
            {
                { new StringContent(publicId), "public_id" },
                { new StringContent(_settings.ApiKey!), "api_key" },
                { new StringContent(timestamp), "timestamp" },
                { new StringContent(signature), "signature" }
            };

            var http = _httpFactory.CreateClient("cloudinary");
            using var response = await http.PostAsync($"{_settings.CloudName}/image/destroy", form, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Cloudinary delete of {PublicId} failed ({Status})", publicId, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Orphaned file at worst - never block the staff action on this.
            _logger.LogWarning(ex, "Cloudinary delete of {PublicId} threw", publicId);
        }
    }

    private string Sign(string paramsToSign)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(paramsToSign + _settings.ApiSecret));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// https://res.cloudinary.com/{cloud}/image/upload/[transformations/][v123/]folder/name.jpg -> "folder/name".
    /// Returns null for URLs that aren't ours, so pasted external links are never "deleted".
    /// </summary>
    internal string? TryGetPublicId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase)) return null;

        var marker = $"/{_settings.CloudName}/image/upload/";
        var path = uri.AbsolutePath;
        var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var segments = path[(idx + marker.Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        // Drop transformation segments (contain commas or look like "w_300") and the version segment (v123456).
        while (segments.Count > 1 &&
               (segments[0].Contains(',') ||
                System.Text.RegularExpressions.Regex.IsMatch(segments[0], @"^[a-z]{1,3}_[^/]+$") ||
                System.Text.RegularExpressions.Regex.IsMatch(segments[0], @"^v\d+$")))
        {
            segments.RemoveAt(0);
        }

        if (segments.Count == 0) return null;
        var joined = Uri.UnescapeDataString(string.Join('/', segments));
        var dot = joined.LastIndexOf('.');
        return dot > 0 ? joined[..dot] : joined;
    }
}
