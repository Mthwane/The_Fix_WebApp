namespace FashionFix.Web.Models.ViewModels;

/// <summary>Drives the shared _Pager partial (1 2 3 ... page links). RouteValues carries the active
/// filters so clicking a page number keeps them.</summary>
public class PagerModel
{
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalItems { get; set; }
    public int PageSize { get; set; } = 10;
    public string Action { get; set; } = "Index";
    public string? Controller { get; set; }
    public Dictionary<string, string?> RouteValues { get; set; } = new();
    /// <summary>"staff" or "store" - picks the CSS flavour so the same partial works in both layouts.</summary>
    public string Theme { get; set; } = "staff";

    public static int ClampPage(int? requested, int totalPages) =>
        Math.Min(Math.Max(requested ?? 1, 1), Math.Max(totalPages, 1));
}
