using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Admin-editable storefront homepage content. A single row (Id is always 1) - there is only ever one
/// homepage. Program.cs seeds a default row on first run.
///
/// Every block of the homepage is editable from Storefront > Homepage Content: the top utility bar, the
/// promo strip (which can be linked to a Discount), the hero, the feature strip, the department and trending
/// headings, the Style Box, the story block and the boutique banner.
///
/// Repeating groups (stats, features, steps, checklist) are stored as one line per item, "Title | Text", so an
/// admin can add or remove items without a schema change. New text columns are nullable on purpose: a row that
/// existed before this change simply falls back to the built-in default via <see cref="ApplyDefaults"/>, so the
/// homepage never goes blank after the migration. Section switches are stored as "Hide..." flags so the
/// migration default (false) keeps every section visible.
/// </summary>
public class SiteSettings
{
    [Key]
    public int Id { get; set; }

    // ===================== Top utility bar =====================
    public bool HideUtilityBar { get; set; }

    /// <summary>One item per line, shown on the left of the utility bar.</summary>
    [MaxLength(400)]
    public string? UtilityLeftText { get; set; }

    /// <summary>One item per line, shown on the right, before the Help / account links.</summary>
    [MaxLength(400)]
    public string? UtilityRightText { get; set; }

    // ===================== Promo strip (replaces the old fixed banner) =====================
    public bool HidePromoStrip { get; set; }

    /// <summary>Free text shown when the strip isn't linked to a discount (or the linked discount isn't live).</summary>
    [MaxLength(300)]
    public string? PromoStripText { get; set; }

    [MaxLength(40)]
    public string? PromoStripLinkText { get; set; }

    /// <summary>Relative (/Shop) or absolute (https://...) link for the strip's button. Blank = no button.</summary>
    [MaxLength(300)]
    public string? PromoStripLinkUrl { get; set; }

    [MaxLength(9)]
    public string? PromoStripBgColor { get; set; }

    [MaxLength(9)]
    public string? PromoStripTextColor { get; set; }

    /// <summary>When set, the strip shows this discount's banner text / value / code / end date automatically,
    /// and follows its dates (not started, expired or used-up = the strip falls back to the free text, or hides
    /// if <see cref="HidePromoStripWhenDiscountEnds"/> is on).</summary>
    public int? PromoStripDiscountId { get; set; }

    public bool HidePromoStripWhenDiscountEnds { get; set; }

    // ===================== Hero =====================
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

    /// <summary>One stat per line, "Value | Label" (e.g. "100% | Certified Regenerative").</summary>
    [MaxLength(500)]
    public string? HeroStatsText { get; set; }

    // ===================== Feature strip =====================
    public bool HideFeatures { get; set; }

    /// <summary>One feature per line, "Title | Description".</summary>
    [MaxLength(1200)]
    public string? FeaturesText { get; set; }

    // ===================== Departments & Trending headings =====================
    [MaxLength(60)]
    public string? DepartmentsEyebrow { get; set; }

    [MaxLength(120)]
    public string? DepartmentsHeadline { get; set; }

    [MaxLength(400)]
    public string? DepartmentsSubtext { get; set; }

    [MaxLength(60)]
    public string? TrendingEyebrow { get; set; }

    [MaxLength(120)]
    public string? TrendingHeadline { get; set; }

    // ===================== Style Box =====================
    public bool HideStyleBox { get; set; }

    [MaxLength(60)]
    public string? StyleBoxEyebrow { get; set; }

    [MaxLength(120)]
    public string StyleBoxHeadline { get; set; } = "Style Box & Seasonal Subscriptions";

    [MaxLength(400)]
    public string StyleBoxSubheadline { get; set; } = "Receive handpicked seasonal capsules matched to your climate, skin undertone, and body geometry. Keep what elevates your routine, send the rest back complimentary.";

    /// <summary>One step per line, "Title | Description".</summary>
    [MaxLength(800)]
    public string? StyleBoxStepsText { get; set; }

    [MaxLength(40)]
    public string? StyleBoxPrimaryCtaText { get; set; }

    [MaxLength(40)]
    public string? StyleBoxSecondaryCtaText { get; set; }

    // ===================== Story block =====================
    public bool HideStory { get; set; }

    [MaxLength(60)]
    public string? StoryEyebrow { get; set; }

    [MaxLength(120)]
    public string? StoryHeadline { get; set; }

    [MaxLength(800)]
    public string? StoryBody { get; set; }

    [MaxLength(500)]
    public string? StoryImageUrl { get; set; }

    /// <summary>One tick-list item per line, "Title | Description".</summary>
    [MaxLength(1000)]
    public string? StoryChecklistText { get; set; }

    [MaxLength(40)]
    public string? StoryCtaText { get; set; }

    // ===================== Boutique banner =====================
    public bool HideBoutique { get; set; }

    [MaxLength(120)]
    public string? BoutiqueHeadline { get; set; }

    [MaxLength(300)]
    public string? BoutiqueText { get; set; }

    [MaxLength(40)]
    public string? BoutiquePrimaryCtaText { get; set; }

    [MaxLength(40)]
    public string? BoutiqueSecondaryCtaText { get; set; }

    public DateTime DateUpdated { get; set; } = DateTime.UtcNow;

    // ===================== Defaults =====================

    public const string DefaultUtilityLeft = "Cape Town Waterfront \u2022 Sandton City\nCustomer Care: +27 21 000 4499";
    public const string DefaultUtilityRight = "FixRewards: Earn 5% Back";
    public const string DefaultPromoText = "Complimentary Express Delivery on orders of R500 or more \u2022 Members unlock 15% seasonal archive privileges";
    public const string DefaultPromoBg = "#D8C7A9";
    public const string DefaultPromoFg = "#24211B";
    public const string DefaultHeroStats = "100% | Certified Regenerative\n0% | Synthetic Plastics\nR500+ | Free Express Logistics";
    public const string DefaultFeatures =
        "Free Express Delivery | Complimentary nationwide courier dispatch on orders of R500 or more. A flat R100 applies below that.\n" +
        "Ethical Craftsmanship | Regenerative botanical textiles and closed-loop herbal dyes.\n" +
        "60-Day Easy Returns | Effortless doorstep exchanges with carbon-neutral parcel returns.\n" +
        "24/7 Style Concierge | Personalized sizing matrix and wardrobe curation on call.";
    public const string DefaultStyleBoxSteps =
        "Take the Quiz | Define silhouettes, fabric weights, and weekly rituals.\n" +
        "Atelier Curates | 5 tailored garments hand-inspected in Cape Town.\n" +
        "Keep or Swap | 7 days to trial at home with 20% bundle reduction.";
    public const string DefaultStoryChecklist =
        "Microbiome-Safe Fabric Treatments | Finished exclusively with probiotic cleansers rather than heavy formaldehyde stabilizers.\n" +
        "Regenerative African Agriculture | Sourced through regional partner conservancies across Southern Africa replenishing topsoil integrity.\n" +
        "Lifetime Mend & Rewear Program | Return any piece at year three for natural re-dyeing or atelier re-stitching at no charge.";

    /// <summary>Fills any blank text field with its built-in default, so a row saved before a field existed (or a
    /// field an admin cleared) never renders as an empty hole. Call on a no-tracking copy for display - never on an
    /// entity you are about to SaveChanges.</summary>
    public SiteSettings ApplyDefaults()
    {
        static string D(string? v, string d) => string.IsNullOrWhiteSpace(v) ? d : v;

        UtilityLeftText = D(UtilityLeftText, DefaultUtilityLeft);
        UtilityRightText = D(UtilityRightText, DefaultUtilityRight);
        PromoStripText = D(PromoStripText, DefaultPromoText);
        PromoStripLinkText = D(PromoStripLinkText, "Shop now");
        PromoStripLinkUrl = D(PromoStripLinkUrl, "/Shop");
        PromoStripBgColor = D(PromoStripBgColor, DefaultPromoBg);
        PromoStripTextColor = D(PromoStripTextColor, DefaultPromoFg);
        HeroStatsText = D(HeroStatsText, DefaultHeroStats);
        FeaturesText = D(FeaturesText, DefaultFeatures);
        DepartmentsEyebrow = D(DepartmentsEyebrow, "Atelier Departments");
        DepartmentsHeadline = D(DepartmentsHeadline, "Curated Department Portals");
        DepartmentsSubtext = D(DepartmentsSubtext, "Explore intentional wardrobes categorized by tactile performance, botanical composition, and versatile ease.");
        TrendingEyebrow = D(TrendingEyebrow, "Weekly Botanical Laboratory");
        TrendingHeadline = D(TrendingHeadline, "Trending This Week");
        StyleBoxEyebrow = D(StyleBoxEyebrow, "The Curated Fix Experience");
        StyleBoxStepsText = D(StyleBoxStepsText, DefaultStyleBoxSteps);
        StyleBoxPrimaryCtaText = D(StyleBoxPrimaryCtaText, "Take the Style Quiz");
        StyleBoxSecondaryCtaText = D(StyleBoxSecondaryCtaText, "View Sample Autumn Box");
        StoryEyebrow = D(StoryEyebrow, "Our Scientific Ethos");
        StoryHeadline = D(StoryHeadline, "Garments Conceived as Living Ecosystems");
        StoryBody = D(StoryBody, "Fashion Fix was founded on a singular botanical conviction: human dermal layers absorb the essence of what touches them. By abandoning petrochemical synthetics in favour of bio-fermented enzymes, raw flax, and plant-derived pigments, our garments support both personal well-being and ecological longevity.");
        StoryChecklistText = D(StoryChecklistText, DefaultStoryChecklist);
        StoryCtaText = D(StoryCtaText, "Read The Transparency Ledger");
        BoutiqueHeadline = D(BoutiqueHeadline, "Visit Our Boutiques in Cape Town & Sandton");
        BoutiqueText = D(BoutiqueText, "Experience the tactile linen weight and bespoke sizing consultations in person.");
        BoutiquePrimaryCtaText = D(BoutiquePrimaryCtaText, "Book Fitting Session");
        BoutiqueSecondaryCtaText = D(BoutiqueSecondaryCtaText, "Find Stores");
        return this;
    }

    /// <summary>Splits a "Title | Text" per-line field into pairs. A line with no pipe is a title with no text.</summary>
    public static List<(string Title, string Text)> ParseLines(string? raw)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf('|');
            list.Add(idx < 0 ? (line.Trim(), string.Empty) : (line[..idx].Trim(), line[(idx + 1)..].Trim()));
        }
        return list;
    }

    /// <summary>Plain one-item-per-line fields (utility bar).</summary>
    public static List<string> ParsePlain(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
