using LocalHandler.Models;
using System.Globalization;
using System.Net;
using System.Text;
using OrderOrange.Shared;

namespace LocalHandler.Services;

/// <summary>
/// Builds the customer's bill as the VERY SAME HTML and CSS the web POS renders in the
/// partner panel's invoice designer (ReceiptView.razor + the .rcpt rules from food.css).
/// A WebView2 then prints this, so the paper the till produces is pixel-for-pixel the
/// receipt the owner designed — not an approximation drawn with a printer font.
/// </summary>
public static class HtmlReceipt
{
    /// <summary>The official rial glyph, inline SVG (same as the site's Fmt.RialSvg).</summary>
    private const string RialSvg =
        "<svg class=\"omr-i\" viewBox=\"0 0 922 480\" xmlns=\"http://www.w3.org/2000/svg\" style=\"height:.9em;width:.9em;vertical-align:-.05em;fill:currentColor\">" +
        "<path d=\"M922.01,236.09l-59.34,95.95-333.24-.07c25.34,15.62,52.07,30.01,80.14,40.23,7.12,2.58,30.02,10.71,36.24,10.71h179.3l-56.94,96.38c-12.64.7-651.61-.33-755.19-.49-5.78,0-9.89,0-12.13,0h-.85l55.44-95.88h343.63c.12,0-26.38-33.78-38.46-50.94l-264.7-.49,56.43-95.39h173.31c-.8-61.59,16.04-121.33,49.53-172.7,31.83-48.8,63.2-75.17,125.05-58.28,41.3,11.29,78.52,39.73,107.65,70.21l-36.5,142.79c-1.77.36-12.58-12.88-16.95-17.5-37.63-39.81-102.37-90.88-160.53-66.57-14.35,6-29.47,19.14-30.02,35.91-.73,22.61,27.02,49.43,40.69,65.64l517.44.5Z\"/></svg>";

    /// <summary>The receipt stylesheet, lifted verbatim from food.css (offline, so no site dependency).</summary>
    private const string Css = """
.rcpt{width:302px;background:#fff;color:#111;font-family:Consolas,"Courier New",monospace;font-size:12.5px;line-height:1.45;padding:18px 14px;border:1px solid #e3ded7;border-radius:8px;margin:0 auto}
.rcpt .c{text-align:center}
.rcpt .lg{font-size:30px;line-height:1.2}
.rcpt .nm{font-size:16px;font-weight:800;letter-spacing:.5px}
.rcpt .nm2{font-size:14px;font-weight:800}
.rcpt .sm{font-size:11.5px;color:#333}
.rcpt .xs{font-size:10.5px;color:#666}
.rcpt .dash{border-top:1px dashed #999;margin:8px 0}
.rcpt .row{display:flex;justify-content:space-between;gap:8px}
.rcpt .row.total{font-size:16px;font-weight:800}
.rcpt .item{display:flex;gap:6px;align-items:baseline}
.rcpt .item .q{flex:0 0 26px;font-weight:700}
.rcpt .item .n{flex:1;word-break:break-word}
.rcpt .item .p{flex:0 0 auto;font-variant-numeric:tabular-nums}
.rcpt .inote{margin-inline-start:32px;font-size:11px;color:#555}
.rcpt .wrap{word-break:break-word}
.rcpt .pay{margin-top:6px;font-weight:800;font-size:12.5px}
.rcpt .ttl{font-weight:800;letter-spacing:1.5px;font-size:12px;margin-bottom:4px}
.rcpt .mono{letter-spacing:2px}
.rcpt .bar{height:34px;margin:4px 10px 2px;background:repeating-linear-gradient(90deg,#111 0 2px,transparent 2px 4px,#111 4px 5px,transparent 5px 9px,#111 9px 12px,transparent 12px 14px,#111 14px 15px,transparent 15px 18px)}
.rcpt.f-sans{font-family:"Segoe UI",system-ui,sans-serif}
.rcpt.f-serif{font-family:Georgia,"Times New Roman",serif}
.rcpt.f-cairo{font-family:"Cairo","Segoe UI",sans-serif}
.rcpt.s-small{font-size:11px}
.rcpt.s-small .nm{font-size:14px}
.rcpt.s-small .row.total{font-size:14px}
.rcpt.s-large{font-size:14px}
.rcpt.s-large .nm{font-size:18px}
.rcpt.s-large .row.total{font-size:18px}
.rcpt.p58{width:219px;padding:14px 10px}
.rcpt .rlogo{max-width:130px;max-height:72px;object-fit:contain;filter:grayscale(1) contrast(1.2)}
.rcpt .tag{font-style:italic}
.rcpt .promo{font-weight:700}
.rcpt .dash.sep-dots{border-top:2px dotted #777}
.rcpt .dash.sep-solid{border-top:1.5px solid #222}
.rcpt .sepstars{text-align:center;color:#444;font-size:9px;letter-spacing:2px;margin:7px 0;user-select:none}
.rcpt .row.total.inv-total{background:#111;color:#fff;padding:6px 10px;border-radius:4px;margin:2px -4px}
.rcpt.sp-compact{line-height:1.28}
.rcpt.sp-compact .dash,.rcpt.sp-compact .sepstars{margin:5px 0}
.rcpt.sp-relaxed{line-height:1.75}
.rcpt.sp-relaxed .dash,.rcpt.sp-relaxed .sepstars{margin:12px 0}
.rcpt.hs-big .nm{font-size:21px;letter-spacing:1px;text-transform:uppercase}
.rcpt.hs-boxed .nm{display:inline-block;border:2px solid #111;padding:3px 14px;margin:2px auto 4px}
.rcpt.hs-boxed{text-align:center}
.rcpt.hs-boxed>:not(.c):not(.dash):not(.sepstars):not(table):not(.stamp){text-align:initial}
.rcpt.fr-box{border:2px solid #111!important}
.rcpt.fr-double{border:1px solid #111!important;outline:1px solid #111;outline-offset:-5px}
.rcpt.ink-bold{font-weight:700}
.rcpt.ink-bold .sm,.rcpt.ink-bold .xs{font-weight:600}
.rcpt.ls-small .rlogo{max-width:80px;max-height:44px}
.rcpt.ls-large .rlogo{max-width:190px;max-height:110px}
.rcpt{position:relative}
.rcpt .stamp{position:absolute;left:50%;top:52%;translate:-50% -50%;rotate:-12deg;border:2px double #444;border-radius:7px;padding:3px 12px;font-weight:900;font-size:15px;letter-spacing:2px;color:#444;opacity:.82;pointer-events:none;z-index:5;white-space:nowrap}
.rcpt .stamp.paid{color:#1b5e20;border-color:#1b5e20}
.rcpt .ritems{width:100%;border-collapse:collapse;margin:2px 0}
.rcpt .ritems th{font-size:.82em;text-align:start;border-top:1px solid #111;border-bottom:1px solid #111;padding:3px 2px}
.rcpt .ritems td{padding:3px 2px;vertical-align:top;border-bottom:1px dotted #bbb}
.rcpt .ritems tr:last-child td{border-bottom:none}
.rcpt .ritems .tq{text-align:center;width:14%}
.rcpt .ritems .tp{text-align:end;width:20%;font-variant-numeric:tabular-nums;white-space:nowrap}
.rcpt .qrimg{display:block;margin:6px auto 2px;image-rendering:pixelated}
.rcpt .qrimg.qr-small{width:86px}
.rcpt .qrimg.qr-normal{width:116px}
.rcpt .qrimg.qr-large{width:152px}
.rcpt.p58 .qrimg.qr-small{width:70px}
.rcpt.p58 .qrimg.qr-normal{width:92px}
.rcpt.p58 .qrimg.qr-large{width:118px}
.rcpt{direction:ltr}
""";

    public static string Build(OrderDto order, StoreProfile store, ReceiptDesignDto d)
    {
        bool bilingual = d.LabelStyle == "bilingual";
        string Lb(string en, string ar) => bilingual ? $"{en} / {ar}" : en;

        var b = new StringBuilder();
        var cls = $"rcpt mf-ascii f-{d.Font} s-{d.FontSize} sp-{d.Spacing} hs-{d.HeaderStyle} fr-{d.Frame} ink-{d.Ink} ls-{d.LogoSize}"
                + (d.PaperWidth == 58 ? " p58" : "");
        b.Append($"<div class=\"{cls}\">");

        // stamp
        if (d.Stamp == "auto")
            b.Append($"<div class=\"stamp {(order.IsPaid ? "paid" : "cash")}\">{(order.IsPaid ? "PAID &#10004;" : PayEn(order))}</div>");

        // logo
        if (!string.IsNullOrEmpty(d.Logo))
            b.Append($"<div class=\"c\"><img class=\"rlogo\" src=\"{d.Logo}\" alt=\"\"></div>");

        b.Append($"<div class=\"c nm\">{Enc(store.Name)}</div>");
        if (!string.IsNullOrWhiteSpace(d.HeaderMessage))
            b.Append($"<div class=\"c sm tag\">{Enc(d.HeaderMessage!)}</div>");
        if (d.ShowAddress)
        {
            b.Append($"<div class=\"c sm\">{Enc(store.Area)} &middot; {Enc(store.Street)}</div>");
            b.Append($"<div class=\"c sm\">Tel: <span>{Enc(store.Phone)}</span> &middot; Muscat, Oman</div>");
        }
        if (d.ShowVat && (store.CrNumber.Length > 0 || store.VatNumber.Length > 0))
        {
            var reg = (store.CrNumber.Length > 0 ? $"CR No: {Enc(store.CrNumber)}" : "")
                    + (store.CrNumber.Length > 0 && store.VatNumber.Length > 0 ? " &middot; " : "")
                    + (store.VatNumber.Length > 0 ? $"VAT No: {Enc(store.VatNumber)}" : "");
            b.Append($"<div class=\"c xs\">{reg}</div>");
        }
        b.Append(Sep(d));

        b.Append($"<div class=\"c ttl\">{(bilingual ? "TAX INVOICE &mdash; فاتورة" : "TAX INVOICE")}</div>");
        Row(b, Lb("Order", "الطلب"), $"<b>{Enc(order.Number)}</b>");
        Row(b, Lb("Receipt #", "رقم الإيصال"), order.Id.ToString("D6"));
        Row(b, Lb("Date", "التاريخ"), order.PlacedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        Row(b, Lb("Type", "النوع"), $"{Lb(TypeEn(order), TypeAr(order))} {TypeIcon(order)}");
        if (order.TableName is { Length: > 0 })
            Row(b, Lb("Table", "الطاولة"), $"<b>{Enc(order.TableName!)}</b>");
        b.Append(Sep(d));

        Row(b, Lb("Customer", "العميل"), $"<b>{Enc(order.CustomerName)}</b>");
        if (order.CustomerPhone is { Length: > 0 } ph && ph.Trim('0', ' ', '-').Length > 0)
            Row(b, Lb("Phone", "الهاتف"), Enc(order.CustomerPhone));
        if (IsDelivery(order) && !string.IsNullOrWhiteSpace(order.DeliveryAddress))
            b.Append($"<div class=\"wrap sm\">{Enc(order.DeliveryAddress)}</div>");
        if (d.ShowCourier && order.DriverName is not null)
        {
            Row(b, Lb("Courier", "المندوب"), $"<b>{Enc(order.DriverName)}</b>");
            if (order.DriverPlate is not null) Row(b, Lb("Plate", "اللوحة"), $"<b>{Enc(order.DriverPlate)}</b>");
        }
        b.Append(Sep(d));

        if (d.ItemStyle == "table")
        {
            b.Append("<table class=\"ritems\"><thead><tr>");
            b.Append($"<th>{Lb("ITEM", "الصنف")}</th><th class=\"tq\">{Lb("QTY", "كمية")}</th><th class=\"tp\">{Lb("PRICE", "السعر")}</th><th class=\"tp\">{Lb("AMT", "المبلغ")}</th>");
            b.Append("</tr></thead><tbody>");
            foreach (var it in order.Items)
            {
                b.Append("<tr><td>").Append(Enc(it.Name));
                if (!string.IsNullOrWhiteSpace(it.Notes)) b.Append($"<div class=\"inote\">* {Enc(it.Notes!)}</div>");
                b.Append("</td>");
                b.Append($"<td class=\"tq\">{it.Quantity}</td>");
                b.Append($"<td class=\"tp\">{it.UnitPrice.ToString("0.000", CultureInfo.InvariantCulture)}</td>");
                b.Append($"<td class=\"tp\">{(it.UnitPrice * it.Quantity).ToString("0.000", CultureInfo.InvariantCulture)}</td></tr>");
            }
            b.Append("</tbody></table>");
        }
        else
        {
            foreach (var it in order.Items)
            {
                b.Append($"<div class=\"item\"><span class=\"q\">{it.Quantity}x</span><span class=\"n\">{Enc(it.Name)}</span><span class=\"p\">{Rial(it.UnitPrice * it.Quantity)}</span></div>");
                if (!string.IsNullOrWhiteSpace(it.Notes)) b.Append($"<div class=\"inote\">* {Enc(it.Notes!)}</div>");
            }
        }
        b.Append(Sep(d));

        Row(b, Lb("Items", "العناصر"), order.Items.Sum(i => i.Quantity).ToString());
        Row(b, Lb("Subtotal", "المجموع"), Rial(order.Subtotal));
        if (order.DeliveryFee > 0) Row(b, Lb("Delivery", "التوصيل"), Rial(order.DeliveryFee));
        if (order.ServiceFee > 0) Row(b, Lb("Service", "الخدمة"), Rial(order.ServiceFee));
        if (order.TaxAmount > 0) Row(b, Lb($"VAT {order.TaxPercent:0.#}%", $"الضريبة {order.TaxPercent:0.#}٪"), Rial(order.TaxAmount));
        if (order.Discount > 0) Row(b, Lb("Discount", "الخصم"), "-" + Rial(order.Discount));
        b.Append(Sep(d));
        b.Append($"<div class=\"row total {(d.TotalStyle == "invert" ? "inv-total" : "")}\"><span>{Lb("TOTAL", "الإجمالي")}</span><span>{Rial(order.Total)}</span></div>");
        Row(b, Lb("Payment", "الدفع"), Lb(PayEn(order), PayAr(order)));
        b.Append($"<div class=\"c pay\">{(order.IsPaid ? $"*** PAID &mdash; {PayEn(order)} ***" : $"*** {PayEn(order)} &mdash; COLLECT {Money(order.Total)} ***")}</div>");

        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            b.Append(Sep(d));
            b.Append($"<div class=\"wrap sm\">{Lb("Note", "ملاحظة")}: {Enc(order.Notes!)}</div>");
        }

        if (d.ShowBarcode)
        {
            b.Append(Sep(d));
            b.Append("<div class=\"bar\"></div>");
            b.Append($"<div class=\"c xs mono\">*{Enc(order.Number)}*</div>");
        }
        if (d.ShowQr && Qr(order, store, d) is { } qr)
        {
            b.Append($"<div class=\"c\"><img class=\"qrimg qr-{d.QrSize}\" src=\"{qr}\" alt=\"QR\"></div>");
            b.Append($"<div class=\"c xs\">{Enc(QrCaption(d))}</div>");
            if (d.QrMode == "verify") b.Append($"<div class=\"c xs mono\">{Enc(BillCode.For(order.Id))}</div>");
        }
        b.Append(Sep(d));
        b.Append($"<div class=\"c sm\">{Enc(string.IsNullOrWhiteSpace(d.FooterMessage) ? "Thank you — see you soon!" : d.FooterMessage!)}</div>");
        if (!string.IsNullOrWhiteSpace(d.Promo)) b.Append($"<div class=\"c sm promo\">{Enc(d.Promo!)}</div>");
        b.Append("<div class=\"c sm\">Keep this receipt for your order.</div>");
        b.Append("<div class=\"c nm2\">OrderOrange</div>");
        b.Append($"<div class=\"c xs\">Printed {DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}</div>");
        b.Append("</div>");

        return "<!doctype html><html><head><meta charset=\"utf-8\">"
             + "<style>*{box-sizing:border-box}html,body{margin:0;padding:0;background:#fff}"
             + "@media print{@page{margin:0}.rcpt{border:none!important;border-radius:0;margin:0}}"
             + Css + "</style></head><body>" + b + "</body></html>";
    }

    private const string TicketCss = """
.tk{margin:0 auto;font-family:"Segoe UI",Arial,sans-serif;color:#000;padding:8px 6px 6px;width:270px}
.tk.p58{width:196px}
.tk .no{text-align:center;font-size:26px;font-weight:900;letter-spacing:.5px;line-height:1.05}
.tk .dt{text-align:center;font-size:12px;color:#333;margin:2px 0 8px}
.tk .who{display:flex;justify-content:space-between;gap:8px;border:2px solid #000;border-radius:9px;padding:5px 8px;margin:0 0 8px;font-weight:800;font-size:14px}
.tk .who span:only-child{margin:0 auto}
.tk .knote{border:2px dashed #000;border-radius:8px;padding:6px 9px;margin:0 0 8px;font-size:15px;font-weight:800;text-align:center}
.tk .hr{border-top:2px solid #000;margin:6px 0}
.tk .it{display:flex;gap:9px;align-items:flex-start;padding:7px 0;border-bottom:1px dashed #999}
.tk .it:last-of-type{border-bottom:none}
.tk .q{flex:0 0 26px;height:26px;line-height:26px;text-align:center;border-radius:50%;background:#000;color:#fff;font-size:15px;font-weight:900}
.tk .b{flex:1;min-width:0}
.tk .n{font-size:15px;font-weight:800;line-height:1.2}
.tk .ds{font-size:11.5px;color:#444;margin-top:2px;line-height:1.3}
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

    /// <summary>
    /// A kitchen / bar work ticket, sized big and legible for the pass: the invoice
    /// number, the time, and each product's quantity, name and description (plus the
    /// line note). No prices, no branding — printed through the same WebView2 path so it
    /// fills the paper at a readable size.
    /// </summary>
    public static string BuildTicket(OrderDto order, IEnumerable<OrderItemDto> items,
        IReadOnlyDictionary<string, string>? descriptions, int paperWidth,
        IReadOnlyDictionary<string, string>? nameOverrides = null,
        IReadOnlyDictionary<int, LineChange>? changes = null,
        TicketStrings? strings = null)
    {
        strings ??= TicketStrings.For("");
        var edited = changes is { Count: > 0 };
        var b = new StringBuilder();
        b.Append($"<div class=\"tk{(paperWidth == 58 ? " p58" : "")}\"{(strings.Rtl ? " dir=\"rtl\"" : "")}>");
        b.Append($"<div class=\"no\">#{Enc(order.Number)}</div>");
        b.Append($"<div class=\"dt\" dir=\"ltr\">{order.PlacedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}</div>");
        // An edit of an invoice the kitchen already has is announced before anything else.
        if (edited) b.Append($"<div class=\"ed\">✎ {Enc(strings.Edited)}</div>");
        // Who it is for: the table and the person, boxed so the pass reads it first.
        var who = new List<string>();
        if (order.TableName is { Length: > 0 }) who.Add(Enc(TableLabel(order.TableName!, strings.Table)));
        if (!string.IsNullOrWhiteSpace(order.CustomerName)) who.Add(Enc(order.CustomerName!.Trim()));
        if (who.Count > 0) b.Append("<div class=\"who\">" + string.Join("", who.Select(w => $"<span>{w}</span>")) + "</div>");
        // The cashier's note to the kitchen, if any.
        if (!string.IsNullOrWhiteSpace(order.Notes))
            b.Append($"<div class=\"knote\">{Enc(order.Notes!.Trim())}</div>");
        b.Append("<div class=\"hr\"></div>");
        foreach (var it in items)
        {
            var name = nameOverrides?.GetValueOrDefault(it.Name) is { Length: > 0 } n ? n : it.Name;
            var ch = changes?.GetValueOrDefault(it.Id);
            var cls = ch is null ? "it" : ch.IsRemoved ? "it rm" : ch.IsNew ? "it nw" : ch.Delta > 0 ? "it up" : "it dn";
            b.Append($"<div class=\"{cls}\">");
            b.Append($"<div class=\"q\">{(ch is { IsRemoved: true } ? "✕" : it.Quantity.ToString())}</div><div class=\"b\">");
            b.Append($"<div class=\"n\">{Enc(name)}</div>");
            // The edit, spelled out: 1 → 3 (+2), 3 → 1 (−2), 5 → 0 REMOVED, or NEW.
            if (ch is not null)
            {
                b.Append("<div class=\"chg\">");
                if (!ch.IsNew) b.Append($"<span class=\"was\" dir=\"ltr\">{ch.From} → {ch.To}</span>");
                if (!ch.IsNew && !ch.IsRemoved) b.Append($"<span class=\"d\" dir=\"ltr\">{(ch.Delta > 0 ? "+" : "−")}{Math.Abs(ch.Delta)}</span>");
                if (ch.IsRemoved) b.Append($"<span class=\"tag\">{Enc(strings.Removed)}</span>");
                if (ch.IsNew) b.Append($"<span class=\"tag\">{Enc(strings.New)}</span>");
                b.Append("</div>");
            }
            // Only what the cashier/admin typed for this line in the POS — never the menu blurb.
            if (!string.IsNullOrWhiteSpace(it.Notes))
                b.Append($"<div class=\"nt\">{Enc(it.Notes!)}</div>");
            b.Append("</div></div>");
        }
        b.Append("<div class=\"hr\"></div></div>");

        return "<!doctype html><html><head><meta charset=\"utf-8\">"
             + "<style>*{box-sizing:border-box}html,body{margin:0;padding:0;background:#fff}@media print{@page{margin:0}}"
             + TicketCss + "</style></head><body>" + b + "</body></html>";
    }

    // ---------- helpers (mirror ReceiptView.razor) ----------

    /// <summary>Menu descriptions often restate the dish ("Kabab — chargrilled…"); drop that and keep it short.</summary>
    private static string TrimDescription(string d, params string[] names)
    {
        d = d.Trim();
        foreach (var n in names)
        {
            if (string.IsNullOrEmpty(n)) continue;
            if (d.StartsWith(n, StringComparison.OrdinalIgnoreCase))
                d = d[n.Length..].TrimStart(' ', '-', '–', '—', ':', '·', '|');
        }
        if (d.Length > 0) d = char.ToUpperInvariant(d[0]) + d[1..];
        return d.Length > 110 ? d[..107].TrimEnd() + "…" : d;
    }

    /// <summary>"Table 5" from any store naming — no "Table Table 5" when the name already says it.</summary>
    private static string TableLabel(string name, string tableWord = "Table")
    {
        name = name.Trim();
        return name.StartsWith("table", StringComparison.OrdinalIgnoreCase) ? name : $"{tableWord} {name}";
    }

    private static void Row(StringBuilder b, string left, string right) =>
        b.Append($"<div class=\"row\"><span>{left}</span><span>{right}</span></div>");

    private static string Sep(ReceiptDesignDto d) => d.Separator == "stars"
        ? "<div class=\"sepstars\">✱ ✱ ✱ ✱ ✱ ✱ ✱ ✱</div>"
        : $"<div class=\"dash sep-{d.Separator}\"></div>";

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Amount(decimal v)
    {
        var t = v.ToString("0.000", CultureInfo.InvariantCulture);
        if (!t.Contains('.')) return t;
        t = t.TrimEnd('0').TrimEnd('.');
        return t.Length == 0 || t == "-" ? "0" : t;
    }
    private static string Rial(decimal v) => $"{Amount(v)} {RialSvg}";
    private static string Money(decimal v) => $"{Amount(v)} OMR";

    private static bool IsDineIn(OrderDto o) => o.TableName is { Length: > 0 }
        || o.DeliveryAddress?.StartsWith("Dine-in", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsCounter(OrderDto o) => !IsDineIn(o) && o.DeliveryAddress?.Equals("Counter", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsDelivery(OrderDto o) => !IsDineIn(o) && !IsCounter(o) && o.OrderType == OrderType.Delivery;

    private static string TypeEn(OrderDto o) => IsDineIn(o) ? "DINE-IN" : IsCounter(o) ? "COUNTER" : IsDelivery(o) ? "DELIVERY" : "PICKUP";
    private static string TypeAr(OrderDto o) => IsDineIn(o) ? "محلي" : IsCounter(o) ? "كاونتر" : IsDelivery(o) ? "توصيل" : "استلام";
    private static string TypeIcon(OrderDto o) => IsDineIn(o) ? "\U0001F37D️" : IsCounter(o) ? "\U0001F9FE" : IsDelivery(o) ? "\U0001F6F5" : "\U0001F3EA";

    private static string PayEn(OrderDto o) => o.PaymentMethod switch
    { PaymentMethod.CardOnline => "ONLINE", PaymentMethod.CardOnDelivery => "CARD", _ => "CASH" };
    private static string PayAr(OrderDto o) => o.PaymentMethod switch
    { PaymentMethod.CardOnline => "أونلاين", PaymentMethod.CardOnDelivery => "بطاقة", _ => "نقداً" };

    private static string QrCaption(ReceiptDesignDto d) => !string.IsNullOrWhiteSpace(d.QrCaption)
        ? d.QrCaption! : d.QrMode == "verify" ? "Scan to verify this bill" : "Scan me";

    private static string? Qr(OrderDto order, StoreProfile store, ReceiptDesignDto d)
    {
        try
        {
            var link = d.QrMode switch
            {
                "link" when !string.IsNullOrWhiteSpace(d.QrLink) => d.QrLink!,
                "store" => $"{store.ClientUrl.TrimEnd('/')}/restaurant/{store.Id}",
                _ => BillCode.LinkFor(order.Id, store.ClientUrl),
            };
            var dots = d.QrSize switch { "small" => 3, "large" => 6, _ => 4 };
            using var gen = new QRCoder.QRCodeGenerator();
            using var data = gen.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
            var png = new QRCoder.PngByteQRCode(data).GetGraphic(dots);
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
        catch { return null; }
    }
}
