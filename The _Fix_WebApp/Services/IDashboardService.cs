using FashionFix.Web.Models.ViewModels;

namespace FashionFix.Web.Services;

public interface IDashboardService
{
    /// <summary>Builds the dashboard model, computing only the requested sections.
    /// currentUserId is only needed when sections includes Shift (to find that user's own
    /// open/recent shifts) - pass null otherwise.</summary>
    Task<DashboardViewModel> BuildAsync(DashboardSections sections, string? currentUserId = null);

    /// <summary>Builds the trimmed floor-staff dashboard: only the viewer's own shift, till, sales, returns and tickets, plus low stock.</summary>
    Task<EmployeeDashboardViewModel> BuildEmployeeAsync(string currentUserId, bool showTill, bool showLowStock, bool canRaisePo, bool showReturns, bool showTickets);
}
