using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Turns any report grid into a branded .xlsx. The page that drew the table sends the
/// same rows it just rendered, so the workbook always matches what the partner saw —
/// there is no second query that could quietly disagree with the screen.
///
/// This endpoint formats; it does not read business data. Whatever the caller could
/// see, it may export, and nothing else reaches this class.
/// </summary>
public class ExportController : ApiControllerBase
{
    /// <summary>A guard against a runaway client, not a business rule.</summary>
    private const int MaxRows = 20_000;

    private static readonly XLColor Brand = XLColor.FromHtml("#E04E00");
    private static readonly XLColor BrandSoft = XLColor.FromHtml("#FFF1E6");
    private static readonly XLColor Ink = XLColor.FromHtml("#3D2F22");
    private static readonly XLColor Muted = XLColor.FromHtml("#8A7A68");
    private static readonly XLColor Hair = XLColor.FromHtml("#EFE4D6");

    [HttpPost("xlsx")]
    public IActionResult Xlsx([FromBody] ExportSheetRequest req)
    {
        if (req.Columns.Count == 0)
            return BadRequest(new { message = "A sheet needs at least one column." });
        if (req.Rows.Count > MaxRows)
            return BadRequest(new { message = $"Too many rows to export (limit {MaxRows:N0})." });

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(SafeSheetName(req.SheetName ?? req.Title));
        sheet.RightToLeft = req.Rtl;

        var lastCol = req.Columns.Count;

        // ---------- The masthead: who, what, when ----------
        // Four merged rows above the grid. Excel loses this context the moment a
        // sheet is mailed on, which is exactly when it matters most.
        sheet.Range(1, 1, 1, lastCol).Merge();
        sheet.Cell(1, 1).Value = req.StoreName;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(Ink);

        sheet.Range(2, 1, 2, lastCol).Merge();
        sheet.Cell(2, 1).Value = req.Title;
        sheet.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(Brand);

        sheet.Range(3, 1, 3, lastCol).Merge();
        sheet.Cell(3, 1).Value = string.Join("   ·   ",
            new[] { req.Subtitle, req.RangeLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        sheet.Cell(3, 1).Style.Font.SetFontSize(10).Font.SetFontColor(Muted);

        sheet.Range(4, 1, 4, lastCol).Merge();
        sheet.Cell(4, 1).Value = string.Join("   ·   ", new[]
        {
            string.IsNullOrWhiteSpace(req.UserName) ? null : $"Prepared by {req.UserName}",
            $"Generated {DateTime.Now:dd MMM yyyy HH:mm}",
            "OrderOrange by MajidTek",
        }.Where(s => s is not null));
        sheet.Cell(4, 1).Style.Font.SetFontSize(9).Font.SetItalic().Font.SetFontColor(Muted);

        sheet.Row(1).Height = 22;
        sheet.Row(4).Height = 16;

        if (TryDecodeLogo(req.LogoData) is { } logo)
        {
            using var stream = new MemoryStream(logo);
            // Anchored to the last column so it never sits on top of the title text.
            sheet.AddPicture(stream)
                 .MoveTo(sheet.Cell(1, Math.Max(1, lastCol)), 6, 2)
                 .WithSize(58, 58);
            sheet.Row(1).Height = 30;
            sheet.Row(2).Height = 22;
        }

        // ---------- Header row ----------
        const int headerRow = 6;
        for (var c = 0; c < req.Columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = req.Columns[c].Header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
            cell.Style.Fill.SetBackgroundColor(Brand);
            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            cell.Style.Alignment.SetHorizontal(req.Columns[c].Numeric
                ? XLAlignmentHorizontalValues.Right
                : XLAlignmentHorizontalValues.Left);
        }
        sheet.Row(headerRow).Height = 20;

        // ---------- Body ----------
        var r = headerRow + 1;
        foreach (var row in req.Rows)
        {
            for (var c = 0; c < req.Columns.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                var text = c < row.Count ? row[c] : "";

                // A numeric column holds a number when the text parses as one. When it
                // does not — an em dash for "no data", a mixed "3 / 5" — the text goes
                // in as-is rather than silently becoming zero.
                if (req.Columns[c].Numeric && TryNumber(text, out var number))
                {
                    cell.Value = number;
                    cell.Style.NumberFormat.SetFormat(HasFraction(number) ? "#,##0.000" : "#,##0");
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                }
                else
                {
                    cell.Value = text;
                }

                cell.Style.Font.SetFontColor(Ink);
                cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
                cell.Style.Border.SetBottomBorderColor(Hair);
            }

            // Banding, so a wide row stays readable across the page.
            if ((r - headerRow) % 2 == 0)
                sheet.Range(r, 1, r, lastCol).Style.Fill.SetBackgroundColor(BrandSoft);

            r++;
        }

        // ---------- Totals ----------
        if (req.Totals is { Count: > 0 })
        {
            for (var c = 0; c < req.Columns.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                var text = c < req.Totals.Count ? req.Totals[c] : "";
                if (req.Columns[c].Numeric && TryNumber(text, out var number))
                {
                    cell.Value = number;
                    cell.Style.NumberFormat.SetFormat(HasFraction(number) ? "#,##0.000" : "#,##0");
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                }
                else
                {
                    cell.Value = text;
                }
                cell.Style.Font.SetBold().Font.SetFontColor(Ink);
                cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFE0C7"));
                cell.Style.Border.SetTopBorder(XLBorderStyleValues.Medium);
                cell.Style.Border.SetTopBorderColor(Brand);
            }
        }

        // ---------- Make it usable ----------
        // Freeze under the header and turn on filters, so a thousand-row sheet is
        // something the partner can actually work in rather than only look at.
        sheet.SheetView.Freeze(headerRow, 0);
        if (req.Rows.Count > 0)
            sheet.Range(headerRow, 1, headerRow + req.Rows.Count, lastCol).SetAutoFilter();

        for (var c = 1; c <= lastCol; c++)
        {
            var want = req.Columns[c - 1].Width;
            if (want > 0) sheet.Column(c).Width = want;
        }
        sheet.Columns(1, lastCol).AdjustToContents(headerRow, headerRow + req.Rows.Count + 1, 10, 46);

        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.PageOrientation = lastCol > 5 ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.Margins.SetTop(0.5).SetBottom(0.5).SetLeft(0.4).SetRight(0.4);
        sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);

        using var output = new MemoryStream();
        book.SaveAs(output);

        var name = $"{Slug(req.Title)}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        return File(output.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>Parses a display value back to a number, tolerating thousands separators,
    /// a currency word, a percent sign and Arabic/Persian digits.</summary>
    private static bool TryNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var cleaned = new string(text.Select(ch => ch switch
        {
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')), // Arabic-Indic
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')), // Persian
            _ => ch,
        }).Where(ch => char.IsDigit(ch) || ch is '.' or '-' or '−').ToArray())
            .Replace('−', '-');

        return cleaned.Length > 0
            && cleaned != "-"
            && double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static bool HasFraction(double value) => Math.Abs(value % 1) > 0.0000001;

    /// <summary>Pulls the bytes out of a data: URI. PNG and JPEG only — ClosedXML cannot
    /// place an SVG, so a store flying an SVG logo simply exports without one.</summary>
    private static byte[]? TryDecodeLogo(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri)) return null;
        var comma = dataUri.IndexOf(',');
        if (comma < 0) return null;
        var head = dataUri[..comma];
        if (!head.Contains("image/png", StringComparison.OrdinalIgnoreCase)
            && !head.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase)
            && !head.Contains("image/jpg", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!head.Contains("base64", StringComparison.OrdinalIgnoreCase)) return null;

        try { return Convert.FromBase64String(dataUri[(comma + 1)..]); }
        catch (FormatException) { return null; }
    }

    /// <summary>Excel rejects \/?*[] and anything over 31 characters in a tab name.</summary>
    private static string SafeSheetName(string raw)
    {
        var cleaned = new string(raw.Where(ch => !"\\/?*[]:".Contains(ch)).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Report";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    private static string Slug(string raw)
    {
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "report" : slug;
    }
}
