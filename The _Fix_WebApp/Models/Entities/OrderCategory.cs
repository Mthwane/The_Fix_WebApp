namespace FashionFix.Web.Models.Entities;

/// <summary>
/// Quick-filter buckets shown as tabs on the Orders screens (staff Orders/Index and the
/// customer-facing Customer/Orders). These group the seven granular OrderStatus values into
/// three buckets that are meaningful to whoever's scanning the list, and are mutually
/// exclusive - every OrderStatus falls into exactly one:
///   - Pending:   Pending, Processing, Shipped   (still moving - needs attention)
///   - Completed: Completed, Delivered           (successfully fulfilled)
///   - Past:      Cancelled, Returned             (closed out, but not fulfilled)
/// Shared here (rather than duplicated per controller) so staff and customers always see the
/// same definition of "pending" vs "completed" vs "past".
/// </summary>
public enum OrderCategory { Pending, Completed, Past }

public static class OrderCategorizer
{
    public static readonly IReadOnlyDictionary<OrderCategory, OrderStatus[]> StatusesFor = new Dictionary<OrderCategory, OrderStatus[]>
    {
        [OrderCategory.Pending] = new[] { OrderStatus.Pending, OrderStatus.Processing, OrderStatus.Shipped },
        [OrderCategory.Completed] = new[] { OrderStatus.Completed, OrderStatus.Delivered },
        [OrderCategory.Past] = new[] { OrderStatus.Cancelled, OrderStatus.Returned },
    };

    /// <summary>Which tab a given status belongs to - used to badge/group orders that aren't pre-filtered by category.</summary>
    public static OrderCategory CategoryOf(OrderStatus status) =>
        StatusesFor.First(kv => kv.Value.Contains(status)).Key;
}