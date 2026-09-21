using OrderOrange.Shared;

namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// One door to the receipt printer. Any component that settles an invoice — the POS,
/// the open-tabs list, the tables board, the assistant — calls <see cref="PrintAsync"/>
/// and the layout does the rest: the Settings gate, the Android/desktop split, the
/// hidden slip. Without this, every closing path grew (or forgot) its own printing.
/// </summary>
public class ReceiptPrinter
{
    /// <summary>The layout's handler; the only subscriber.</summary>
    public event Func<OrderDto, Task>? Fire;

    public Task PrintAsync(OrderDto order) => Fire?.Invoke(order) ?? Task.CompletedTask;
}
