using System.Globalization;
using System.Net;
using OrderOrange.AndroidTill.Models;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill.Printing;

/// <summary>HTML in, paper out: render, convert to 1-bit raster, send as ESC/POS, N copies.</summary>
public static class PrintService
{
    /// <summary>The CSS width of a kitchen ticket's root block for a paper width.</summary>
    public static int TicketCssWidth(int paper) => paper == 58 ? 196 : 270;

    /// <summary>Returns null on success, else why not.</summary>
    public static async Task<string?> PrintHtmlAsync(PrinterConfig printer, string html, int cssWidth, CancellationToken ct = default)
    {
        try
        {
            var s = AppSettings.Current;
            using var bitmap = await HtmlRenderer.RenderAsync(html, printer.WidthDots, cssWidth);
            var bytes = EscPos.Encode(bitmap, s.Dither, s.Threshold, feedLines: 4, cut: printer.Cut);
            var transport = PrinterTransports.For(printer);
            for (var copy = 0; copy < Math.Max(1, printer.Copies); copy++)
                await transport.SendAsync(bytes, ct);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// The station test page: Latin, Arabic, Persian and Urdu text plus a QR, so a missing
    /// font, a wrong paper width or a code-page problem shows at once.
    /// </summary>
    public static string TestHtml(PrinterConfig p)
    {
        var w = TicketCssWidth(p.Paper);
        var qr = HtmlReceipt.QrDataUri("https://orderorange.com", 4);
        string E(string t) => WebUtility.HtmlEncode(t);
        return "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
               "*{box-sizing:border-box}html,body{margin:0;padding:0;background:#fff}" +
               $".tt{{width:{w}px;margin:0 auto;font-family:\"Segoe UI\",Arial,sans-serif;color:#000;padding:8px 6px}}" +
               ".tt .h{text-align:center;font-size:20px;font-weight:900}.tt .s{text-align:center;font-size:12px;color:#333;margin:2px 0 8px}" +
               ".tt .hr{border-top:2px solid #000;margin:6px 0}.tt .l{font-size:15px;font-weight:700;padding:3px 0}" +
               ".tt .rtl{direction:rtl;text-align:right}.tt .inv{background:#000;color:#fff;padding:4px 8px;border-radius:6px;text-align:center;font-weight:900}" +
               ".tt .q{display:block;margin:8px auto 2px;width:120px;image-rendering:pixelated}.tt .c{text-align:center;font-size:11px}" +
               "</style></head><body><div class=\"tt\">" +
               "<div class=\"h\">OrderOrange Till</div>" +
               $"<div class=\"s\">{E(p.Name)} · {E(p.Target)} · {p.Paper} mm · {DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)}</div>" +
               "<div class=\"hr\"></div>" +
               "<div class=\"l\">English: The quick brown fox — 0123456789</div>" +
               "<div class=\"l rtl\">العربية: مرحباً بكم في أوردر أورنج ٠١٢٣٤٥٦٧٨٩</div>" +
               "<div class=\"l rtl\">فارسی: به اوردر اورنج خوش آمدید ۰۱۲۳۴۵۶۷۸۹</div>" +
               "<div class=\"l rtl\">اردو: اوردر اورنج میں خوش آمدید</div>" +
               "<div class=\"hr\"></div>" +
               "<div class=\"inv\">INVERTED BLOCK · كتلة معكوسة</div>" +
               $"<img class=\"q\" src=\"{qr}\" alt=\"QR\"><div class=\"c\">orderorange.com</div>" +
               "<div class=\"hr\"></div>" +
               "<div class=\"c\">If the Arabic lines show boxes, add an Arabic font (see Resources/Raw).</div>" +
               "</div></body></html>";
    }
}
