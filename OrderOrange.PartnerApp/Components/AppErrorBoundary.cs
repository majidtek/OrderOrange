using Microsoft.AspNetCore.Components.Web;
using OrderOrange.PartnerApp.Till.Services;

namespace OrderOrange.PartnerApp.Components;

/// <summary>
/// Catches an error inside a page so the app stays alive: the page shows a card with
/// "Try again" instead of the whole app going blank, and the error goes into the till log
/// (Printers → View log) so it can be read later.
/// </summary>
public sealed class AppErrorBoundary : ErrorBoundary
{
    protected override Task OnErrorAsync(Exception exception)
    {
        TillLog.Write($"page error: {exception}");
        return base.OnErrorAsync(exception);
    }
}
