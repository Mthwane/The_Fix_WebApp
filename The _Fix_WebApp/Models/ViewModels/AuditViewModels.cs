namespace FashionFix.Web.Models.ViewModels;

public class AuditRowViewModel
{
    public int Id { get; set; }
    public string Date { get; set; } = string.Empty;      // 2026/10/03
    public string Time { get; set; } = string.Empty;      // 03:49:30
    public string ActorName { get; set; } = string.Empty;
    public string ActorInitials { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public bool ActorIsSystem { get; set; }
    public string Action { get; set; } = string.Empty;
    public string CategoryLabel { get; set; } = string.Empty;
    public string CategoryCss { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Hash { get; set; }
}

public class AuditStatsViewModel
{
    public int Total { get; set; }
    public int Last24Hours { get; set; }
    public int Logistics { get; set; }
    public int AutoBooked { get; set; }
    public int AuthEvents { get; set; }
    public int FailedSignIns { get; set; }
    public int Sealed { get; set; }
    public int Legacy { get; set; }
    public string? HeadHash { get; set; }
}

public class AuditLogsPageViewModel
{
    public List<AuditRowViewModel> Rows { get; set; } = new();
    public AuditStatsViewModel Stats { get; set; } = new();

    // Active filters (echoed back into the form and the pager links)
    public string? Q { get; set; }
    public string Category { get; set; } = "all";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    // Paging
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalFiltered { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalFiltered / (double)PageSize));

    /// <summary>Event count per category key (plus "all" and "other") for the filter chips.</summary>
    public Dictionary<string, int> CategoryCounts { get; set; } = new();
}
