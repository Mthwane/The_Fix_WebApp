using System.Security.Claims;
using System.Text.Json;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Address suggestions for the address search box, served from Geoapify. The browser talks to THIS
/// controller, not to Geoapify, which means:
///   * the Geoapify key stays on the server (never in the page source);
///   * only signed-in people can use it, with a per-user limit, so nobody can drain the free daily credits;
///   * repeated searches are answered from a short cache and don't cost a credit.
/// Each cache-miss costs one Geoapify credit (one request = one credit on their API).
/// </summary>
[Authorize]
public class AddressSearchController : Controller
{
    private const int MinQueryLength = 3;
    private const int MaxQueryLength = 120;
    private const int MaxRequestsPerMinute = 40;
    private const int ResultLimit = 5;
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly AddressAutocompleteSettings _settings;
    private readonly ILogger<AddressSearchController> _logger;

    public AddressSearchController(
        IHttpClientFactory httpFactory,
        IMemoryCache cache,
        IOptions<AddressAutocompleteSettings> settings,
        ILogger<AddressSearchController> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public record AddressSuggestion(
        string Label, string Line1, string Line2,
        string Street, string Suburb, string City, string Province, string Postal);

    // GET: /AddressSearch/Suggest?q=12 smith street durban
    [HttpGet]
    public async Task<IActionResult> Suggest(string? q, CancellationToken ct)
    {
        if (_settings.ResolveProvider(null) != "geoapify")
            return NotFound();

        var query = string.Join(' ', (q ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (query.Length < MinQueryLength || query.Length > MaxQueryLength)
            return Json(new { items = Array.Empty<AddressSuggestion>() });

        // Per-user limit (a person typing generates a handful of searches a minute, not dozens).
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var counter = _cache.GetOrCreate($"addr-rate:{userId}:{DateTime.UtcNow:yyyyMMddHHmm}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return new int[1];
        })!;
        if (Interlocked.Increment(ref counter[0]) > MaxRequestsPerMinute)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "rate" });

        var cacheKey = $"addr-suggest:{query.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out List<AddressSuggestion>? cached) && cached is not null)
            return Json(new { items = cached });

        try
        {
            var http = _httpFactory.CreateClient("geoapify");
            var url = "geocode/autocomplete"
                      + $"?text={Uri.EscapeDataString(query)}"
                      + "&filter=countrycode:za&format=json&lang=en"
                      + $"&limit={ResultLimit}"
                      + $"&apiKey={Uri.EscapeDataString(_settings.GeoapifyApiKey!)}";

            using var response = await http.GetAsync(url, ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogError("Geoapify rejected the API key (HTTP {Status}). Check AddressAutocomplete:GeoapifyApiKey.", (int)response.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "key" });
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Geoapify rate/credit limit reached.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "limit" });
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Geoapify autocomplete returned HTTP {Status}.", (int)response.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "upstream" });
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var items = new List<AddressSuggestion>();
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in results.EnumerateArray())
                {
                    var houseNumber = Str(r, "housenumber");
                    var street = Str(r, "street");
                    var streetLine = string.Join(' ', new[] { houseNumber, street }.Where(s => s.Length > 0));

                    var label = Str(r, "formatted");
                    if (label.Length == 0) continue;

                    var suburb = Str(r, "suburb");
                    if (suburb.Length == 0) suburb = Str(r, "district");

                    var city = Str(r, "city");
                    if (city.Length == 0) city = Str(r, "town");
                    if (city.Length == 0) city = Str(r, "village");
                    if (city.Length == 0) city = Str(r, "county");

                    items.Add(new AddressSuggestion(
                        label,
                        Str(r, "address_line1"),
                        Str(r, "address_line2"),
                        streetLine,
                        suburb,
                        city,
                        Str(r, "state"),
                        Str(r, "postcode")));
                }
            }

            _cache.Set(cacheKey, items, CacheFor);
            return Json(new { items });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new EmptyResult(); // the person kept typing; the browser abandoned this request
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Address search request failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "upstream" });
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? string.Empty).Trim()
            : string.Empty;
}
