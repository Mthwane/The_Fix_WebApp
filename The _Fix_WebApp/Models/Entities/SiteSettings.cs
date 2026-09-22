using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Admin-editable storefront homepage copy. Deliberately a single row (Id is always 1) -
/// there's only ever one homepage. Program.cs seeds a default row on first run so the
/// homepage always has something to render even before an admin has touched this screen.
/// Scope note: covers the hero banner and the Style Box banner only for now - the feature
/// strip captions, ethics/story block, and boutique banner are still hardcoded in the view
/// (backlog item, not part of this pass).
/// </summary>
public class SiteSettings
{
    [Key]
    public int Id { get; set; } 

    [MaxLength(80)]
    public string HeroEyebrow { get; set; } = "SS26 Conservatoire Edition \u2022 Vol. 04";

    [MaxLength(120)]
    public string HeroHeadline { get; set; } = "Elevate Your Everyday Essentials";

    [MaxLength(400)]
    public string HeroSubheadline { get; set; } = "Curated conscious silhouettes, refined organic fabrics, and versatile modern tailoring by Fashion Fix. Botanical dyes designed to breathe with you.";

    [MaxLength(500)]
    public string? HeroImageUrl { get; set; }

    [MaxLength(40)]
    public string HeroPrimaryCtaText { get; set; } = "Shop The Fix";

    [MaxLength(40)]
    public string HeroSecondaryCtaText { get; set; } = "Explore Lookbook";

    [MaxLength(120)]
    public string StyleBoxHeadline { get; set; } = "Style Box & Seasonal Subscriptions";

    [MaxLength(400)]
    public string StyleBoxSubheadline { get; set; } = "Receive handpicked seasonal capsules matched to your climate, skin undertone, and body geometry. Keep what elevates your routine, send the rest back complimentary.";

    public DateTime DateUpdated { get; set; } = DateTime.UtcNow;
}
