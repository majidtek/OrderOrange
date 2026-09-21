using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace LocalHandler.Services;

/// <summary>
/// Prints receipt HTML through an off-screen WebView2, so the paper is rendered by the
/// same engine (Edge/Chromium) the web POS uses. One hidden browser is kept alive and
/// reused; each print navigates to the HTML and sends it straight to the named printer
/// with no dialog. If WebView2 is missing or fails, the caller falls back to the plain
/// text printer.
/// </summary>
public sealed class WebReceiptPrinter
{
    private readonly Window _owner;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _web;
    private TaskCompletionSource<bool>? _navDone;

    public WebReceiptPrinter(Window owner) => _owner = owner;

    private async Task EnsureAsync()
    {
        if (_web is not null) return;

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrderOrange", "wv2");
        Directory.CreateDirectory(dataDir);

        var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
        var hwnd = new WindowInteropHelper(_owner).EnsureHandle();
        _controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        _controller.IsVisible = false;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, 400, 800);
        _web = _controller.CoreWebView2;
        _web.NavigationCompleted += (_, e) => _navDone?.TrySetResult(e.IsSuccess);
    }

    /// <summary>Renders <paramref name="html"/> and prints it to <paramref name="printerName"/>. Null on success, else why not.</summary>
    public async Task<string?> PrintAsync(string html, string printerName, int paperWidthMm)
    {
        try
        {
            await EnsureAsync();

            _navDone = new TaskCompletionSource<bool>();
            _web!.NavigateToString(html);
            // Don't hang the till forever if the engine stalls.
            var ok = await Task.WhenAny(_navDone.Task, Task.Delay(8000)) == _navDone.Task && _navDone.Task.Result;
            if (!ok) return "The receipt page did not render.";

            // Let the QR image and fonts settle, then measure the real content height.
            await Task.Delay(120);
            var heightPx = await MeasureHeightAsync();

            var mmToIn = 1.0 / 25.4;
            var settings = _web.Environment.CreatePrintSettings();
            settings.PrinterName = printerName;
            settings.ShouldPrintBackgrounds = true;   // the black barcode, inverted total and stamp
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0;
            settings.PageWidth = paperWidthMm * mmToIn;
            settings.PageHeight = Math.Max(1.0, (heightPx / 96.0) + 0.15);   // roll paper: page = content, no blank feed

            var status = await _web.PrintAsync(settings);
            return status == CoreWebView2PrintStatus.Succeeded ? null
                 : status == CoreWebView2PrintStatus.PrinterUnavailable ? $"Printer “{printerName}” is unavailable."
                 : "The printer refused the receipt.";
        }
        catch (Exception ex)
        {
            return ex.Message;   // most often: the WebView2 runtime is not installed
        }
    }

    private async Task<double> MeasureHeightAsync()
    {
        try
        {
            var js = await _web!.ExecuteScriptAsync(
                "Math.ceil((document.querySelector('.rcpt')||document.body).getBoundingClientRect().height)");
            return double.TryParse(js?.Trim('"'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0 ? h : 1000;
        }
        catch { return 1000; }
    }
}
