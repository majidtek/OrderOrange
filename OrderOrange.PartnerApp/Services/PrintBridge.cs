using Microsoft.JSInterop;
using OrderOrange.PartnerApp.Till.Services;

namespace OrderOrange.PartnerApp.Services;

/// <summary>
/// The portal's print buttons, answered by the app's own printers. index.html sends the
/// receipt the page shows (drawn with html2canvas, as the browser portal already does for
/// thermal printers) here instead of to the browser's print dialog or the RawBT app.
/// </summary>
public static class PrintBridge
{
    /// <param name="NoPrinter">No cashier printer is set up: the page may fall back to the system print dialog.</param>
    public sealed record Reply(bool Ok, string? Error, bool NoPrinter = false);

    /// <summary>Paper width in dots of the cashier printer, so the page draws the receipt at the right size. 0 = none set up.</summary>
    [JSInvokable]
    public static int CashierDots() => TillEngine.Instance.Settings.Cashier?.WidthDots ?? 0;

    [JSInvokable]
    public static async Task<Reply> PrintSlip(string pngBase64)
    {
        if (TillEngine.Instance.Settings.Cashier is null) return new Reply(false, null, NoPrinter: true);
        try
        {
            var comma = pngBase64.IndexOf(',');
            var png = Convert.FromBase64String(comma >= 0 ? pngBase64[(comma + 1)..] : pngBase64);
            var err = await TillEngine.Instance.PrintSlipImageAsync(png);
            return new Reply(err is null, err);
        }
        catch (Exception ex)
        {
            return new Reply(false, ex.Message);
        }
    }
}
