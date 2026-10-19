using System.Globalization;
using System.Net;
using System.Text;

namespace OrderOrange.Shared;

/// <summary>
/// How a store's kitchen / bar ticket looks (Majed 2026-10-13: "in designer add kitchen UI
/// also"). Saved per store from the partner Invoice designer, printed by the till.
/// </summary>
/// <param name="TextSize">small | normal | large | xlarge — scales the whole ticket.</param>
/// <param name="NumberSize">normal | big | huge — the order number at the top.</param>
/// <param name="Separator">line | dashed | double | none.</param>
public record KitchenDesignDto(
    string TextSize = "normal",
    string NumberSize = "big",
    bool ShowStation = true,
    bool ShowTime = true,
    bool ShowTable = true,
    bool ShowCustomer = true,
    bool ShowNotes = true,
    bool ShowItemNotes = true,
    bool UpperCase = false,
    string Separator = "line",
    string Header = "",
    string Footer = "")
{
    public static KitchenDesignDto Default => new();
}

/// <summary>One line's quantity before and after an edit of an open invoice. From 0 = a
/// line added to an invoice the kitchen already has; To 0 = the line was struck.</summary>
public sealed record LineChange(int From, int To)
{
    public bool IsNew => From == 0;
    public bool IsRemoved => To == 0;
    public int Delta => To - From;
}

/// <summary>
/// The few words a kitchen ticket prints on its own, in the shop's kitchen language —
/// so a Farsi kitchen reads "میز" and "فاکتور ویرایش شد", not "Table" and "EDITED".
/// </summary>
public sealed record TicketStrings(string Table, string Edited, string New, string Removed, bool Rtl = false)
{
    public static TicketStrings For(string? lang) => (lang ?? "").Trim().ToLowerInvariant() switch
    {
        "ar" => new("طاولة", "تم تعديل الفاتورة", "جديد", "حُذف", true),
        "fa" => new("میز", "فاکتور ویرایش شد", "جدید", "حذف شد", true),
        "ur" => new("میز", "انوائس میں ترمیم", "نیا", "ہٹا دیا", true),
        "tr" => new("Masa", "FATURA DÜZENLENDİ", "YENİ", "KALDIRILDI"),
        "hi" => new("टेबल", "बिल संपादित", "नया", "हटाया"),
        "de" => new("Tisch", "RECHNUNG GEÄNDERT", "NEU", "ENTFERNT"),
        "es" => new("Mesa", "FACTURA EDITADA", "NUEVO", "ELIMINADO"),
        "fr" => new("Table", "FACTURE MODIFIÉE", "NOUVEAU", "SUPPRIMÉ"),
        "it" => new("Tavolo", "CONTO MODIFICATO", "NUOVO", "RIMOSSO"),
        "ja" => new("テーブル", "伝票変更", "新規", "削除"),
        "pt" => new("Mesa", "FATURA EDITADA", "NOVO", "REMOVIDO"),
        "ru" => new("Стол", "СЧЁТ ИЗМЕНЁН", "НОВОЕ", "УДАЛЕНО"),
        "zh" => new("桌", "账单已修改", "新增", "已删除"),
        _ => new("Table", "INVOICE EDITED", "NEW", "REMOVED"),
    };
}

/// <summary>
/// The kitchen / bar work ticket as one HTML document — the till prints it through WebView2
/// and the partner designer shows the very same document as its live preview, so what the
/// owner designs is what the pass gets. No prices, no branding unless the owner adds a header.
/// </summary>
public static class KitchenTicket
{
    /// <summary>One line of a ticket: what to make and how many.</summary>
    public sealed record Line(int Id, int Quantity, string Name, string? Notes);

    public static string Html(OrderDto order, IEnumerable<OrderItemDto> items, int paperWidth,
        KitchenDesignDto? design = null, string? stationName = null,
        IReadOnlyDictionary<string, string>? nameOverrides = null,
        IReadOnlyDictionary<int, LineChange>? changes = null,
        TicketStrings? strings = null) =>
        Html(order.Number, order.PlacedAt, order.TableName, order.CustomerName, order.Notes,
            items.Select(i => new Line(i.Id, i.Quantity, i.Name, i.Notes)), paperWidth, design, stationName,
            nameOverrides, changes, strings);

    /// <summary>The same ticket from plain values — what the partner designer previews with.</summary>
    public static string Html(string number, DateTime placedAt, string? tableName, string? customerName, string? notes,
        IEnumerable<Line> items, int paperWidth,
        KitchenDesignDto? design = null, string? stationName = null,
        IReadOnlyDictionary<string, string>? nameOverrides = null,
        IReadOnlyDictionary<int, LineChange>? changes = null,
        TicketStrings? strings = null)
    {
        var d = design ?? KitchenDesignDto.Default;
        strings ??= TicketStrings.For("");
        var zoom = d.TextSize switch { "small" => 0.9, "large" => 1.15, "xlarge" => 1.3, _ => 1.0 };
        var width = (paperWidth == 58 ? 196 : 270) / zoom;
        var sep = d.Separator is "dashed" or "double" or "none" ? d.Separator : "line";
        var num = d.NumberSize is "normal" or "huge" ? d.NumberSize : "big";
        var edited = changes is { Count: > 0 };

        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture,
            $"<div class=\"tk sep-{sep} num-{num}{(d.UpperCase ? " up" : "")}\" style=\"zoom:{zoom:0.##};width:{width:0}px\"{(strings.Rtl ? " dir=\"rtl\"" : "")}>");
        if (d.ShowStation && !string.IsNullOrWhiteSpace(stationName))
            b.Append($"<div class=\"st\">{Enc(stationName.Trim())}</div>");
        if (!string.IsNullOrWhiteSpace(d.Header))
            b.Append($"<div class=\"hd\">{Enc(d.Header.Trim())}</div>");
        b.Append($"<div class=\"no\">#{Enc(number)}</div>");
        if (d.ShowTime)
            b.Append($"<div class=\"dt\" dir=\"ltr\">{placedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}</div>");
        // An edit of an invoice the kitchen already has is announced before anything else.
        if (edited) b.Append($"<div class=\"ed\">✎ {Enc(strings.Edited)}</div>");
        // Who it is for: the table and the person, boxed so the pass reads it first.
        var who = new List<string>();
        if (d.ShowTable && tableName is { Length: > 0 }) who.Add(Enc(TableLabel(tableName, strings.Table)));
        if (d.ShowCustomer && !string.IsNullOrWhiteSpace(customerName)) who.Add(Enc(customerName.Trim()));
        if (who.Count > 0) b.Append("<div class=\"who\">" + string.Join("", who.Select(w => $"<span>{w}</span>")) + "</div>");
        if (d.ShowNotes && !string.IsNullOrWhiteSpace(notes))
            b.Append($"<div class=\"knote\">{Enc(notes.Trim())}</div>");
        b.Append("<div class=\"hr\"></div>");
        foreach (var it in items)
        {
            var name = nameOverrides?.GetValueOrDefault(it.Name) is { Length: > 0 } n ? n : it.Name;
            var ch = changes?.GetValueOrDefault(it.Id);
            var cls = ch is null ? "it" : ch.IsRemoved ? "it rm" : ch.IsNew ? "it nw" : ch.Delta > 0 ? "it up" : "it dn";
            b.Append($"<div class=\"{cls}\">");
            b.Append($"<div class=\"q\">{(ch is { IsRemoved: true } ? "✕" : it.Quantity.ToString(CultureInfo.InvariantCulture))}</div><div class=\"b\">");
            b.Append($"<div class=\"n\">{Enc(name)}</div>");
            if (ch is not null)
            {
                b.Append("<div class=\"chg\">");
                if (!ch.IsNew) b.Append($"<span class=\"was\" dir=\"ltr\">{ch.From} → {ch.To}</span>");
                if (!ch.IsNew && !ch.IsRemoved) b.Append($"<span class=\"d\" dir=\"ltr\">{(ch.Delta > 0 ? "+" : "−")}{Math.Abs(ch.Delta)}</span>");
                if (ch.IsRemoved) b.Append($"<span class=\"tag\">{Enc(strings.Removed)}</span>");
                if (ch.IsNew) b.Append($"<span class=\"tag\">{Enc(strings.New)}</span>");
                b.Append("</div>");
            }
            // Only what the cashier typed for this line in the POS — never the menu blurb.
            if (d.ShowItemNotes && !string.IsNullOrWhiteSpace(it.Notes))
                b.Append($"<div class=\"nt\">{Enc(it.Notes!)}</div>");
            b.Append("</div></div>");
        }
        b.Append("<div class=\"hr\"></div>");
        if (!string.IsNullOrWhiteSpace(d.Footer))
            b.Append($"<div class=\"ft\">{Enc(d.Footer.Trim())}</div>");
        b.Append("</div>");

        return "<!doctype html><html><head><meta charset=\"utf-8\">"
             + "<style>*{box-sizing:border-box}html,body{margin:0;padding:0;background:#fff}@media print{@page{margin:0}}"
             + Css + "</style></head><body>" + b + "</body></html>";
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    /// <summary>"Table 5" from any store naming — no "Table Table 5" when the name already says it.</summary>
    private static string TableLabel(string name, string tableWord = "Table")
    {
        name = name.Trim();
        return name.StartsWith("table", StringComparison.OrdinalIgnoreCase) || name.StartsWith("🚗") ? name : $"{tableWord} {name}";
    }

    private const string Css = """
.tk{margin:0 auto;font-family:"Segoe UI",Arial,sans-serif;color:#000;padding:8px 6px 6px;width:270px}
.tk .st{margin:0 0 6px;padding:4px 8px;border-radius:7px;background:#000;color:#fff;text-align:center;font-size:14px;font-weight:900;letter-spacing:1px;text-transform:uppercase}
.tk .hd{text-align:center;font-size:13px;font-weight:800;margin:0 0 4px}
.tk .ft{text-align:center;font-size:12px;font-weight:700;margin:6px 0 0}
.tk .no{text-align:center;font-size:26px;font-weight:900;letter-spacing:.5px;line-height:1.05}
.tk.num-normal .no{font-size:20px}
.tk.num-huge .no{font-size:36px}
.tk .dt{text-align:center;font-size:12px;color:#333;margin:2px 0 8px}
.tk .who{display:flex;justify-content:space-between;gap:8px;border:2px solid #000;border-radius:9px;padding:5px 8px;margin:0 0 8px;font-weight:800;font-size:14px}
.tk .who span:only-child{margin:0 auto}
.tk .knote{border:2px dashed #000;border-radius:8px;padding:6px 9px;margin:0 0 8px;font-size:15px;font-weight:800;text-align:center}
.tk .hr{border-top:2px solid #000;margin:6px 0}
.tk.sep-dashed .hr{border-top-style:dashed}
.tk.sep-double .hr{border-top:4px double #000}
.tk.sep-none .hr{border-top:0;margin:3px 0}
.tk .it{display:flex;gap:9px;align-items:flex-start;padding:7px 0;border-bottom:1px dashed #999}
.tk .it:last-of-type{border-bottom:none}
.tk .q{flex:0 0 26px;height:26px;line-height:26px;text-align:center;border-radius:50%;background:#000;color:#fff;font-size:15px;font-weight:900}
.tk .b{flex:1;min-width:0}
.tk .n{font-size:15px;font-weight:800;line-height:1.2}
.tk.up .n{text-transform:uppercase}
.tk .nt{display:inline-block;margin-top:4px;padding:2px 8px;border-radius:6px;background:#000;color:#fff;font-size:13px;font-weight:800}
.tk .ed{margin:2px 0 8px;padding:6px 8px;border-radius:8px;background:#000;color:#fff;text-align:center;font-size:15px;font-weight:900;letter-spacing:.4px}
.tk .it.rm .n{text-decoration:line-through;color:#333}
.tk .it.rm .q{background:#fff;color:#000;border:2px solid #000;line-height:22px}
.tk .it.nw .q{background:#fff;color:#000;border:2px dashed #000;line-height:22px}
.tk .chg{display:flex;gap:6px;align-items:center;flex-wrap:wrap;margin-top:4px}
.tk .was{font-size:13px;font-weight:800;padding:1px 8px;border:2px solid #000;border-radius:999px;unicode-bidi:isolate}
.tk .d{font-size:14px;font-weight:900;unicode-bidi:isolate}
.tk .tag{font-size:12px;font-weight:900;padding:2px 8px;border-radius:999px;background:#000;color:#fff;letter-spacing:.3px}
""";
}
