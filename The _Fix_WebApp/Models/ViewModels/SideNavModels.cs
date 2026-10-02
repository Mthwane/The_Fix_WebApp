namespace FashionFix.Web.Models.ViewModels;

/// <summary>One link in the sidebar. Permission / Role are optional gates; a link with neither is visible to everyone who can see the sidebar.</summary>
public record SideNavItem(
    string Label,
    string Icon,
    string Controller,
    string Action,
    string? Permission = null,
    string? Role = null,
    string? BadgeKey = null,
    string[]? OnlyActions = null,      // highlight only when the current action is one of these
    string[]? ExceptActions = null);   // never highlight when the current action is one of these

public record SideNavSection(string? Title, List<SideNavItem> Items);

public class SideNavViewModel
{
    /// <summary>"staff" or "account" - picks the colour theme and the brand block.</summary>
    public string Mode { get; set; } = "staff";
    public string BrandTitle { get; set; } = "FashionFix";
    public string BrandSubtitle { get; set; } = string.Empty;
    public List<SideNavSection> Sections { get; set; } = new();
    public Dictionary<string, string> Badges { get; set; } = new();
    public string UserName { get; set; } = string.Empty;
    public string UserSubtitle { get; set; } = string.Empty;
    public string Initials { get; set; } = "?";
    public string CurrentController { get; set; } = string.Empty;
    public string CurrentAction { get; set; } = string.Empty;
}
