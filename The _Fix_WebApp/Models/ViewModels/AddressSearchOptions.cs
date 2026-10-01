namespace FashionFix.Web.Models.ViewModels;

/// <summary>
/// Tells the shared address search box (Views/Shared/_AddressSearch.cshtml) which form fields to fill
/// when someone picks a suggestion. Each value is the HTML id of an input already on the page
/// (asp-for="City" renders id="City"). Only StreetId is required; leave any other id null and that
/// part of the address is simply not filled.
/// </summary>
public class AddressSearchOptions
{
    /// <summary>Street number + street name (e.g. "12 Smith Street").</summary>
    public string StreetId { get; set; } = string.Empty;

    /// <summary>Suburb / local area.</summary>
    public string? SuburbId { get; set; }

    public string? CityId { get; set; }
    public string? ProvinceId { get; set; }
    public string? PostalCodeId { get; set; }

    /// <summary>Two-letter country code suggestions are limited to.</summary>
    public string Country { get; set; } = "za";

    public string Label { get; set; } = "Search for your address";
}
