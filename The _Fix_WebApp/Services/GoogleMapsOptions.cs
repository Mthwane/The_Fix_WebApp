namespace FashionFix.Web.Services;

/// <summary>
/// Bound from the "GoogleMaps" configuration section. The key is used in the BROWSER (it is sent to
/// Google by the address autocomplete script), so it must be a browser key restricted in Google Cloud
/// to your site's domains AND to the Maps JavaScript API + Places API only. Leave it empty to switch
/// address autocomplete off - every address form keeps working as plain typed fields.
/// Keep the real value out of appsettings.json: dotnet user-secrets set "GoogleMaps:ApiKey" "..."
/// </summary>
public class GoogleMapsOptions
{
    public string? ApiKey { get; set; }
}
