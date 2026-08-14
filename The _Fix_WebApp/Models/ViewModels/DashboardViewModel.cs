using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>Backs the store manager / owner dashboard (US-04, US-18, US-19).</summary>
public class DashboardViewModel
{
    public decimal TodaysSales { get; set; }
    public int TodaysOrderCount { get; set; }
    public decimal MonthToDateRevenue { get; set; }

    public int TotalActiveProducts { get; set; }
    public int LowStockCount { get; set; }
    public List<Product> LowStockProducts { get; set; } = new();

    public List<Product> BestSellers { get; set; } = new();
    public List<Order> RecentOrders { get; set; } = new();
}
