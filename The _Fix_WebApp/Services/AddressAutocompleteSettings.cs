namespace FashionFix.Web.Services;

/// <summary>
/// Bound from the "AddressAutocomplete" configuration section. Decides which service powers the address
/// search box (Views/Shared/_AddressSearch.cshtml).
///
///   Provider = "Geoapify"  -> suggestions come from Geoapify, fetched by THIS server (AddressSearchController),
///                             so the key never reaches the browser. Free plan, no card needed.
///   Provider = "Google"    -> Google Places in the browser, using GoogleMaps:ApiKey (needs a billing account).
///   Provider empty         -> automatic: Geoapify if its key is set, otherwise Google if its key is set.
///   No keys at all         -> no search box; every address form works as plain typed fields.
///
/// Keep keys out of appsettings.json:  dotnet user-secrets set "AddressAutocomplete:GeoapifyApiKey" "..."
/// (in production use the environment variable AddressAutocomplete__GeoapifyApiKey).
/// </summary>
public class AddressAutocompleteSettings
{
    public string? Provider { get; set; }
    public string? GeoapifyApiKey { get; set; }

    /// <summary>"geoapify", "google", or "" when nothing usable is configured.</summary>
    public string ResolveProvider(string? googleApiKey)
    {
        var chosen = (Provider ?? string.Empty).Trim().ToLowerInvariant();

        if (chosen == "geoapify") return string.IsNullOrWhiteSpace(GeoapifyApiKey) ? "" : "geoapify";
        if (chosen == "google") return string.IsNullOrWhiteSpace(googleApiKey) ? "" : "google";

        if (!string.IsNullOrWhiteSpace(GeoapifyApiKey)) return "geoapify";
        if (!string.IsNullOrWhiteSpace(googleApiKey)) return "google";
        return "";
    }
}
