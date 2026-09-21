using OrderOrange.Shared;

namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// A live-board filter, deliberately simple: the customer's name plus the order's
/// stage. A record so the dialog can edit a copy and hand back a clean result.
/// </summary>
public sealed record OrderFilter
{
    public string? Query { get; set; }
    public string? Stage { get; set; }     // null | new | kitchen | out

    public bool IsActive => !string.IsNullOrWhiteSpace(Query) || Stage is not null;

    public bool Matches(OrderDto o)
    {
        if (!string.IsNullOrWhiteSpace(Query) &&
            !(o.CustomerName?.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        return Stage switch
        {
            "new" => o.Status == OrderStatus.Pending,
            "kitchen" => o.Status is OrderStatus.Accepted or OrderStatus.Preparing,
            "out" => o.Status is OrderStatus.Ready or OrderStatus.PickedUp or OrderStatus.OnTheWay,
            _ => true,
        };
    }
}
