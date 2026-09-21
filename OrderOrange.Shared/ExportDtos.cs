namespace OrderOrange.Shared;

// ---------- Report export ----------
//
// Every report in the portal is, underneath, the same shape: a title, a period, a
// header row and a grid of strings. Rather than teach the server about each report,
// the page that already knows how to render its own table hands that grid over and
// asks for a workbook back. One endpoint serves all of them, and a new report gets
// Excel and print for free the day it is written.

/// <summary>One column of an exported grid.</summary>
/// <param name="Header">The label shown in the sheet's header row.</param>
/// <param name="Numeric">Right-align and store as a number so Excel can sum the column.</param>
/// <param name="Width">Preferred width in characters; 0 lets the builder size it.</param>
public record ExportColumnDto(string Header, bool Numeric = false, double Width = 0);

/// <summary>
/// Everything needed to build a branded workbook. Values arrive already formatted
/// for display; numeric columns are re-parsed on the server so the cell holds a real
/// number rather than text — a report you cannot sum in Excel is a screenshot.
/// </summary>
public record ExportSheetRequest(
    string Title,
    string? Subtitle,
    string? RangeLabel,
    string StoreName,
    string? UserName,
    List<ExportColumnDto> Columns,
    List<List<string>> Rows,
    List<string>? Totals = null,
    string? LogoData = null,
    string? SheetName = null,
    bool Rtl = false);
