using System.Linq.Expressions;
using FashionFix.Web.Models.Entities;

namespace FashionFix.Web.Services.Audit;

/// <summary>
/// Groups audit actions into the filter chips on the Audit Logs screen. Matching is by action-name
/// prefix, so new actions that follow the existing naming (e.g. "ProductArchived") land in the
/// right group without touching this file.
/// </summary>
public static class AuditCategories
{
    public record Category(string Key, string Label, string[] Prefixes, string Css);

    public static readonly Category[] All =
    {
        new("auth",      "Authentication & Accounts", new[] { "Login", "Logout", "Profile", "Password", "Customer" }, "aud-c-auth"),
        new("logistics", "Shipments & Logistics",     new[] { "Shipment", "AutoBook", "Courier" },                    "aud-c-log"),
        new("sales",     "Sales & Orders",            new[] { "Sale", "Shift", "OnlineOrder", "Order", "Return", "Wallet", "PaymentIncident", "CancelRefund" }, "aud-c-sales"),
        new("catalogue", "Catalogue & Inventory",     new[] { "Product", "Variant", "Batch", "PurchaseOrder", "Restock" }, "aud-c-cat"),
        new("admin",     "Admin & Security",          new[] { "Employee", "Role", "Rewards", "Support", "Audit", "Pricing" }, "aud-c-admin"),
    };

    public static readonly Category Other = new("other", "Other", Array.Empty<string>(), "aud-c-other");

    public static Category Find(string? key) =>
        All.FirstOrDefault(c => c.Key == key) ?? Other;

    /// <summary>Category for display. First matching group wins.</summary>
    public static Category For(string action) =>
        All.FirstOrDefault(c => c.Prefixes.Any(p => action.StartsWith(p, StringComparison.Ordinal))) ?? Other;

    /// <summary>Applies a category filter to a query (translates to SQL LIKE 'prefix%').</summary>
    public static IQueryable<AuditLog> Filter(IQueryable<AuditLog> query, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key == "all") return query;

        var p = Expression.Parameter(typeof(AuditLog), "a");
        var action = Expression.Property(p, nameof(AuditLog.Action));
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;

        Expression Match(IEnumerable<string> prefixes) =>
            prefixes.Select(x => (Expression)Expression.Call(action, startsWith, Expression.Constant(x)))
                    .Aggregate(Expression.OrElse);

        Expression body;
        if (key == Other.Key)
            body = Expression.Not(Match(All.SelectMany(c => c.Prefixes)));
        else
        {
            var cat = All.FirstOrDefault(c => c.Key == key);
            if (cat is null) return query;
            body = Match(cat.Prefixes);
        }

        return query.Where(Expression.Lambda<Func<AuditLog, bool>>(body, p));
    }
}
