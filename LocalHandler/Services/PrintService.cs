using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;
using LocalHandler.Models;
using OrderOrange.Shared;

namespace LocalHandler.Services;

/// <summary>
/// Puts ink on paper. The customer's bill is composed to mirror, line for line, the
/// receipt the owner laid out in the partner panel's invoice designer (the same
/// <see cref="ReceiptDesignDto"/> the web POS prints from) — logo, header and footer
/// messages, address, tax numbers, the item style, the total style, separators,
/// barcode and QR — so the paper this till prints is the paper the owner designed.
/// Kitchen tickets stay a plain work list: no prices, no branding.
/// </summary>
public static class PrintService
{
    /// <summary>Every printer Windows can see on this machine.</summary>
    public static List<string> InstalledPrinters()
    {
        var names = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters) names.Add(name);
        return names;
    }

    // ======================================================================
    //  The composed receipt: a list of blocks plus the paper-wide choices the
    //  owner made. Print() turns blocks into ink; ToPreview() into on-screen text.
    // ======================================================================

    public enum Align { Left, Centre, Right }

    public abstract record Block;
    /// <summary>One centred/left/right line, optionally bold and scaled off the base size.</summary>
    public sealed record LineBlock(string Text, Align Align = Align.Left, bool Bold = false, float Scale = 1f) : Block;
    /// <summary>A label on the left and a value on the right, justified to the paper edges.</summary>
    public sealed record RowBlock(string Left, string Right, bool Bold = false, bool Invert = false) : Block;
    /// <summary>A four-column item line for the "table" item style.</summary>
    public sealed record ItemRowBlock(string Name, string Qty, string Price, string Amount) : Block;
    /// <summary>A section separator in the owner's chosen style.</summary>
    public sealed record SepBlock(string Style) : Block;
    /// <summary>One or more blank lines.</summary>
    public sealed record GapBlock(int Lines = 1) : Block;
    /// <summary>A centred image (the logo, or the QR) sized to a height in millimetres.</summary>
    public sealed record ImageBlock(byte[] Data, double HeightMm) : Block;
    /// <summary>The decorative barcode strip the receipt shows under the totals.</summary>
    public sealed record BarBlock(string Caption) : Block;

    /// <summary>A finished receipt, ready to print or preview.</summary>
    public sealed record Receipt(
        IReadOnlyList<Block> Blocks,
        string FontFamily,
        float BaseSize,
        float LineFactor,
        bool Frame,
        int Columns)
    {
        /// <summary>A plain-text rendering for the on-screen preview.</summary>
        public string ToPreview()
        {
            var w = Math.Clamp(Columns, 24, 64);
            var sb = new StringBuilder();
            foreach (var block in Blocks)
            {
                switch (block)
                {
                    case LineBlock l:
                        sb.AppendLine(Place(l.Text, l.Align, w));
                        break;
                    case RowBlock r:
                        sb.AppendLine(Justify(r.Left, r.Right, w));
                        break;
                    case ItemRowBlock it:
                        sb.AppendLine(Justify($"{it.Qty}x {it.Name}", it.Amount, w));
                        break;
                    case SepBlock s:
                        sb.AppendLine(s.Style == "stars" ? Place("* * * * * * *", Align.Centre, w)
                            : s.Style == "none" ? ""
                            : new string(RuleChar(s.Style), w));
                        break;
                    case GapBlock g:
                        for (var i = 0; i < g.Lines; i++) sb.AppendLine();
                        break;
                    case ImageBlock:
                        sb.AppendLine(Place("[ image ]", Align.Centre, w));
                        break;
                    case BarBlock b:
                        sb.AppendLine(Place("|| ||| | || |||| ||", Align.Centre, w));
                        sb.AppendLine(Place($"*{b.Caption}*", Align.Centre, w));
                        break;
                }
            }
            return sb.ToString();
        }
    }

    // ======================================================================
    //  Compose the customer's bill from the owner's design.
    // ======================================================================

    /// <summary>The customer's bill, laid out exactly as the owner designed it.</summary>
    public static Receipt BuildReceipt(OrderDto order, Station station, StoreProfile store, ReceiptDesignDto design)
    {
        var cols = design.PaperWidth == 58 ? 32 : 44;
        if (station.Columns is > 0 and < 64) cols = Math.Min(cols, station.Columns);

        var blocks = new List<Block>();
        void Line(string t, Align a = Align.Left, bool b = false, float s = 1f) { if (t is not null) blocks.Add(new LineBlock(t, a, b, s)); }
        void Row(string l, string r, bool b = false, bool inv = false) => blocks.Add(new RowBlock(l, r, b, inv));
        void Sep() => blocks.Add(new SepBlock(design.Separator));
        void Gap(int n = 1) => blocks.Add(new GapBlock(n));

        bool bilingual = design.LabelStyle == "bilingual";
        string Lb(string en, string ar) => bilingual ? $"{en} / {ar}" : en;

        // ---- Stamp across the top (design: Stamp = "auto") ----
        if (design.Stamp == "auto")
            Line(order.IsPaid ? "PAID  ✔" : PayWord(order), Align.Centre, true, 1.15f);

        // ---- Logo (only if the owner uploaded one) ----
        if (TryImage(design.Logo, out var logoPng))
            blocks.Add(new ImageBlock(logoPng, design.LogoSize switch { "small" => 9, "large" => 20, _ => 14 }));

        // ---- Store identity ----
        Line(store.Name, Align.Centre, true, design.HeaderStyle == "big" ? 1.5f : 1.25f);
        if (!string.IsNullOrWhiteSpace(design.HeaderMessage)) Line(design.HeaderMessage!, Align.Centre, false, 0.9f);
        if (design.ShowAddress)
        {
            var where = string.Join(" · ", new[] { store.Area, store.Street }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (where.Length > 0) Line(where, Align.Centre, false, 0.9f);
            if (!string.IsNullOrWhiteSpace(store.Phone)) Line($"Tel: {store.Phone} · Muscat, Oman", Align.Centre, false, 0.9f);
        }
        if (design.ShowVat && (store.CrNumber.Length > 0 || store.VatNumber.Length > 0))
        {
            var reg = (store.CrNumber.Length > 0 ? $"CR No: {store.CrNumber}" : "")
                    + (store.CrNumber.Length > 0 && store.VatNumber.Length > 0 ? " · " : "")
                    + (store.VatNumber.Length > 0 ? $"VAT No: {store.VatNumber}" : "");
            Line(reg, Align.Centre, false, 0.85f);
        }
        Sep();

        // ---- Invoice head ----
        Line(bilingual ? "TAX INVOICE — فاتورة" : "TAX INVOICE", Align.Centre, true, 1.05f);
        Row(Lb("Order", "الطلب"), order.Number, true);
        Row(Lb("Receipt #", "رقم الإيصال"), order.Id.ToString("D6"));
        Row(Lb("Date", "التاريخ"), order.PlacedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        Row(Lb("Type", "النوع"), TypeWord(order, bilingual));
        if (order.TableName is { Length: > 0 }) Row(Lb("Table", "الطاولة"), order.TableName!, true);
        Sep();

        // ---- Who it is for ----
        Row(Lb("Customer", "العميل"), order.CustomerName, true);
        if (order.CustomerPhone is { Length: > 0 } ph && ph.Trim('0', ' ', '-').Length > 0)
            Row(Lb("Phone", "الهاتف"), order.CustomerPhone);
        if (IsDelivery(order) && !string.IsNullOrWhiteSpace(order.DeliveryAddress))
            Line(order.DeliveryAddress, Align.Left, false, 0.9f);
        if (design.ShowCourier && order.DriverName is not null)
        {
            Row(Lb("Courier", "المندوب"), order.DriverName, true);
            if (order.DriverPlate is not null) Row(Lb("Plate", "اللوحة"), order.DriverPlate, true);
        }
        Sep();

        // ---- The lines ----
        if (design.ItemStyle == "table")
        {
            blocks.Add(new ItemRowBlock(Lb("ITEM", "الصنف"), Lb("QTY", "كمية"), Lb("PRICE", "سعر"), Lb("AMT", "مبلغ")));
            foreach (var line in order.Items)
            {
                blocks.Add(new ItemRowBlock(line.Name, line.Quantity.ToString(),
                    line.UnitPrice.ToString("0.000", CultureInfo.InvariantCulture),
                    (line.UnitPrice * line.Quantity).ToString("0.000", CultureInfo.InvariantCulture)));
                if (!string.IsNullOrWhiteSpace(line.Notes)) Line($"  * {line.Notes}", Align.Left, false, 0.85f);
            }
        }
        else
        {
            foreach (var line in order.Items)
            {
                Row($"{line.Quantity}x {line.Name}", Amount(line.UnitPrice * line.Quantity));
                if (!string.IsNullOrWhiteSpace(line.Notes)) Line($"  * {line.Notes}", Align.Left, false, 0.85f);
            }
        }
        Sep();

        // ---- The money ----
        Row(Lb("Items", "العناصر"), order.Items.Sum(i => i.Quantity).ToString());
        Row(Lb("Subtotal", "المجموع"), Amount(order.Subtotal));
        if (order.DeliveryFee > 0) Row(Lb("Delivery", "التوصيل"), Amount(order.DeliveryFee));
        if (order.ServiceFee > 0) Row(Lb("Service", "الخدمة"), Amount(order.ServiceFee));
        if (order.TaxAmount > 0) Row(Lb($"VAT {order.TaxPercent:0.#}%", $"الضريبة {order.TaxPercent:0.#}٪"), Amount(order.TaxAmount));
        if (order.Discount > 0) Row(Lb("Discount", "الخصم"), "-" + Amount(order.Discount));
        Sep();
        Row(Lb("TOTAL", "الإجمالي"), Money(order.Total), true, design.TotalStyle == "invert");
        Row(Lb("Payment", "الدفع"), PayWord(order));
        Line(order.IsPaid ? $"*** PAID — {PayWord(order)} ***" : $"*** {PayWord(order)} — COLLECT {Money(order.Total)} ***",
            Align.Centre, true);

        if (!string.IsNullOrWhiteSpace(order.Notes)) { Sep(); Line($"{Lb("Note", "ملاحظة")}: {order.Notes}", Align.Left, false, 0.9f); }

        // ---- Barcode ----
        if (design.ShowBarcode) { Sep(); blocks.Add(new BarBlock(order.Number)); }

        // ---- QR ----
        if (design.ShowQr && BuildQr(order, store, design) is { } qrPng)
        {
            blocks.Add(new ImageBlock(qrPng, design.QrSize switch { "small" => 18, "large" => 32, _ => 24 }));
            Line(QrCaption(design), Align.Centre, false, 0.85f);
            if (design.QrMode == "verify") Line(BillCode.For(order.Id), Align.Centre, false, 0.8f);
        }
        Sep();

        // ---- Footer ----
        Line(string.IsNullOrWhiteSpace(design.FooterMessage) ? "Thank you — see you soon!" : design.FooterMessage!, Align.Centre, false, 0.9f);
        if (!string.IsNullOrWhiteSpace(design.Promo)) Line(design.Promo!, Align.Centre, false, 0.9f);
        Line("Keep this receipt for your order.", Align.Centre, false, 0.85f);
        Line("OrderOrange", Align.Centre, true, 0.95f);
        Line($"Printed {DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}", Align.Centre, false, 0.8f);
        Gap();

        return new Receipt(blocks, FontFamily(design.Font),
            BaseSize(design.FontSize), LineFactor(design.Spacing), design.Frame != "none", cols);
    }

    // ======================================================================
    //  The kitchen / bar work ticket: what to cook, for whom. No prices.
    // ======================================================================

    /// <param name="descriptions">Product id → menu description, printed under each line.</param>
    public static Receipt BuildTicket(OrderDto order, Station station, IEnumerable<OrderItemDto> items,
        IReadOnlyDictionary<string, string>? descriptions = null,
        IReadOnlyDictionary<string, string>? nameOverrides = null,
        IReadOnlyDictionary<int, LineChange>? changes = null,
        TicketStrings? strings = null)
    {
        strings ??= TicketStrings.For("");
        // Deliberately spare: the invoice number, the time, and for each product its
        // name, quantity and description (plus the customer's note on that line).
        // No prices, no logo, no store branding — the pass needs what to make, not the bill.
        var cols = Math.Clamp(station.Columns, 24, 64);
        var blocks = new List<Block>
        {
            new LineBlock($"#{order.Number}", Align.Centre, true, 1.4f),
            new LineBlock(order.PlacedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), Align.Centre),
        };
        if (changes is { Count: > 0 })
            blocks.Add(new LineBlock($"** {strings.Edited} **", Align.Centre, true, 1.2f));
        if (order.TableName is { Length: > 0 } tn)
            blocks.Add(new LineBlock(tn.Trim().StartsWith("table", StringComparison.OrdinalIgnoreCase) ? tn.Trim() : $"{strings.Table} {tn.Trim()}", Align.Centre, true, 1.1f));
        if (!string.IsNullOrWhiteSpace(order.CustomerName)) blocks.Add(new LineBlock(order.CustomerName.Trim(), Align.Centre, true, 1.1f));
        blocks.Add(new SepBlock("dash"));
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            blocks.Add(new LineBlock($"** {order.Notes!.Trim()} **", Align.Centre, true, 1.1f));
            blocks.Add(new SepBlock("dash"));
        }
        foreach (var item in items)
        {
            var name = nameOverrides?.GetValueOrDefault(item.Name) is { Length: > 0 } n ? n : item.Name;
            var ch = changes?.GetValueOrDefault(item.Id);
            blocks.Add(new LineBlock($"{(ch is { IsRemoved: true } ? "X" : item.Quantity.ToString())} x {name}", Align.Left, true, 1.15f));
            if (ch is not null)
                blocks.Add(new LineBlock(
                    ch.IsRemoved ? $"   {ch.From} -> 0   {strings.Removed}"
                    : ch.IsNew ? $"   {strings.New}"
                    : $"   {ch.From} -> {ch.To}   ({(ch.Delta > 0 ? "+" : "-")}{Math.Abs(ch.Delta)})",
                    Align.Left, true, 1f));
            if (!string.IsNullOrWhiteSpace(item.Notes)) blocks.Add(new LineBlock($"   >> {item.Notes}", Align.Left, true, 0.95f));
            blocks.Add(new GapBlock());
        }
        blocks.Add(new SepBlock("dash"));
        blocks.Add(new GapBlock());
        return new Receipt(blocks, "Consolas", 10f, 1.2f, false, cols);
    }

    /// <summary>A plain text sample (station test print).</summary>
    public static Receipt Sample(Station station) => new(
        new Block[]
        {
            new LineBlock(string.IsNullOrWhiteSpace(station.Name) ? "Test print" : station.Name, Align.Centre, true, 1.2f),
            new SepBlock("dash"),
            new LineBlock("Test print", Align.Centre),
            new LineBlock($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}", Align.Centre),
            new GapBlock(),
            new LineBlock("OrderOrange Local Handler", Align.Centre, false, 0.85f),
            new GapBlock(),
        },
        "Consolas", 10f, 1.2f, false, Math.Clamp(station.Columns, 24, 64));

    // ======================================================================
    //  Ink on paper.
    // ======================================================================

    /// <summary>Sends a composed receipt to a station. Returns null on success, else why not.</summary>
    public static string? Print(Station station, Receipt receipt)
    {
        if (string.IsNullOrWhiteSpace(station.PrinterName)) return "No printer chosen for this station.";
        try
        {
            for (var copy = 0; copy < Math.Max(1, station.Copies); copy++)
            {
                var cursor = 0;
                using var document = new PrintDocument();
                document.PrinterSettings.PrinterName = station.PrinterName;
                if (!document.PrinterSettings.IsValid) return $"Windows does not know the printer “{station.PrinterName}”.";
                document.DocumentName = "OrderOrange receipt";

                document.PrintPage += (_, e) => cursor = RenderPage(e, receipt, cursor);
                document.Print();
            }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Convenience: print a run of plain text as a simple receipt.</summary>
    public static string? Print(Station station, string text)
    {
        var lines = text.Replace("\r", "").Split('\n').Select(l => (Block)new LineBlock(l)).ToList();
        return Print(station, new Receipt(lines, "Consolas", 10f, 1.2f, false, Math.Clamp(station.Columns, 24, 64)));
    }

    /// <summary>Draws blocks from <paramref name="start"/> until the page fills; returns the next index.</summary>
    private static int RenderPage(PrintPageEventArgs e, Receipt r, int start)
    {
        var g = e.Graphics!;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var area = e.MarginBounds;
        float left = area.Left, right = area.Right, top = area.Top, bottom = area.Bottom;

        if (r.Frame)
        {
            using var pen = new Pen(Color.Black, 1f);
            g.DrawRectangle(pen, left - 4, top - 4, (right - left) + 8, (bottom - top) + 8);
        }

        using var baseFont = new Font(r.FontFamily, r.BaseSize, FontStyle.Regular, GraphicsUnit.Point);
        var lineH = baseFont.GetHeight(g) * r.LineFactor;
        var y = top;
        var i = start;

        for (; i < r.Blocks.Count; i++)
        {
            var block = r.Blocks[i];
            var need = BlockHeight(g, block, r, lineH);
            if (y + need > bottom && y > top) break; // spill to next page (but always place at least one)

            switch (block)
            {
                case GapBlock gap:
                    y += lineH * gap.Lines;
                    break;

                case SepBlock sep:
                    DrawSeparator(g, sep.Style, left, right, y, lineH, r, baseFont);
                    y += lineH;
                    break;

                case LineBlock l:
                    using (var f = Scaled(r, l.Scale, l.Bold))
                        DrawAligned(g, l.Text, f, l.Align, left, right, y);
                    y += baseFont.GetHeight(g) * Math.Max(1f, l.Scale) * (r.LineFactor - 0.15f + 0.15f);
                    break;

                case RowBlock row:
                    DrawRow(g, row, r, baseFont, left, right, y, lineH);
                    y += lineH;
                    break;

                case ItemRowBlock it:
                    DrawItemRow(g, it, baseFont, left, right, y);
                    y += lineH;
                    break;

                case BarBlock bar:
                    using (var brush = new SolidBrush(Color.Black))
                        g.FillRectangle(brush, left + (right - left) * 0.15f, y + 2, (right - left) * 0.7f, lineH * 0.55f);
                    y += lineH * 0.7f;
                    using (var f = Scaled(r, 0.85f, false))
                        DrawAligned(g, $"*{bar.Caption}*", f, Align.Centre, left, right, y);
                    y += lineH;
                    break;

                case ImageBlock img:
                    y += DrawImage(g, img, left, right, y);
                    break;
            }
        }

        e.HasMorePages = i < r.Blocks.Count;
        return i;
    }

    // ---------- block drawing ----------

    private static float BlockHeight(Graphics g, Block block, Receipt r, float lineH) => block switch
    {
        GapBlock gap => lineH * gap.Lines,
        LineBlock l => g.MeasureString("Ag", ScaledMetric(r, l.Scale)).Height * r.LineFactor,
        BarBlock => lineH * 1.7f,
        ImageBlock img => (float)(img.HeightMm / 25.4 * g.DpiY) + lineH * 0.4f,
        _ => lineH,
    };

    private static void DrawAligned(Graphics g, string text, Font font, Align align, float left, float right, float y)
    {
        if (string.IsNullOrEmpty(text)) return;
        var w = g.MeasureString(text, font).Width;
        var x = align switch
        {
            Align.Centre => left + ((right - left) - w) / 2f,
            Align.Right => right - w,
            _ => left,
        };
        g.DrawString(text, font, Brushes.Black, Math.Max(left, x), y);
    }

    private static void DrawRow(Graphics g, RowBlock row, Receipt r, Font baseFont, float left, float right, float y, float lineH)
    {
        using var f = new Font(baseFont.FontFamily, baseFont.SizeInPoints, row.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        var rightW = g.MeasureString(row.Right, f).Width;

        if (row.Invert)
        {
            g.FillRectangle(Brushes.Black, left - 2, y - 1, (right - left) + 4, lineH);
            g.DrawString(row.Left, f, Brushes.White, left + 2, y);
            g.DrawString(row.Right, f, Brushes.White, right - rightW - 2, y);
            return;
        }

        var leftText = row.Left;
        var maxLeft = (right - left) - rightW - 6;
        while (leftText.Length > 1 && g.MeasureString(leftText, f).Width > maxLeft)
            leftText = leftText[..(leftText.Length - 2)] + "…";
        g.DrawString(leftText, f, Brushes.Black, left, y);
        g.DrawString(row.Right, f, Brushes.Black, right - rightW, y);
    }

    private static void DrawItemRow(Graphics g, ItemRowBlock it, Font baseFont, float left, float right, float y)
    {
        var span = right - left;
        // name | qty | price | amount  →  55% / 12% / 16% / 17%
        var qtyX = left + span * 0.55f;
        var priceX = left + span * 0.67f;
        var amtRight = right;
        using var f = new Font(baseFont.FontFamily, baseFont.SizeInPoints, FontStyle.Regular, GraphicsUnit.Point);

        var name = it.Name;
        var maxName = span * 0.53f;
        while (name.Length > 1 && g.MeasureString(name, f).Width > maxName)
            name = name[..(name.Length - 2)] + "…";
        g.DrawString(name, f, Brushes.Black, left, y);
        g.DrawString(it.Qty, f, Brushes.Black, qtyX, y);
        g.DrawString(it.Price, f, Brushes.Black, priceX, y);
        var amtW = g.MeasureString(it.Amount, f).Width;
        g.DrawString(it.Amount, f, Brushes.Black, amtRight - amtW, y);
    }

    private static void DrawSeparator(Graphics g, string style, float left, float right, float y, float lineH, Receipt r, Font baseFont)
    {
        var mid = y + lineH / 2f;
        switch (style)
        {
            case "none":
                break;
            case "stars":
                using (var f = new Font(baseFont.FontFamily, baseFont.SizeInPoints, FontStyle.Regular, GraphicsUnit.Point))
                    DrawAligned(g, "✱ ✱ ✱ ✱ ✱ ✱ ✱ ✱", f, Align.Centre, left, right, y);
                break;
            case "double":
                using (var pen = new Pen(Color.Black, 1f))
                {
                    g.DrawLine(pen, left, mid - 1.5f, right, mid - 1.5f);
                    g.DrawLine(pen, left, mid + 1.5f, right, mid + 1.5f);
                }
                break;
            case "dots":
                using (var pen = new Pen(Color.Black, 1f) { DashStyle = DashStyle.Dot })
                    g.DrawLine(pen, left, mid, right, mid);
                break;
            default: // dash
                using (var pen = new Pen(Color.Black, 1f))
                    g.DrawLine(pen, left, mid, right, mid);
                break;
        }
    }

    private static float DrawImage(Graphics g, ImageBlock img, float left, float right, float y)
    {
        try
        {
            using var ms = new MemoryStream(img.Data);
            using var bmp = Image.FromStream(ms);
            var h = (float)(img.HeightMm / 25.4 * g.DpiY);
            var w = h * bmp.Width / bmp.Height;
            var maxW = right - left;
            if (w > maxW) { w = maxW; h = w * bmp.Height / bmp.Width; }
            var x = left + (maxW - w) / 2f;
            g.DrawImage(bmp, x, y + 2, w, h);
            return h + 6;
        }
        catch { return 0; }
    }

    // ---------- design → rendering choices ----------

    private static string FontFamily(string font) => font switch
    {
        "sans" => "Segoe UI",
        "serif" => "Times New Roman",
        _ => "Consolas",
    };

    private static float BaseSize(string size) => size switch { "small" => 8f, "large" => 11.5f, _ => 9.5f };
    private static float LineFactor(string spacing) => spacing switch { "small" => 1.0f, "large" => 1.5f, _ => 1.2f };

    private static Font Scaled(Receipt r, float scale, bool bold) =>
        new(r.FontFamily, r.BaseSize * Math.Max(0.5f, scale), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);

    private static Font ScaledMetric(Receipt r, float scale) =>
        new(r.FontFamily, r.BaseSize * Math.Max(0.5f, scale), FontStyle.Regular, GraphicsUnit.Point);

    private static char RuleChar(string style) => style switch { "double" => '=', "dots" => '.', _ => '-' };

    // ---------- QR / logo ----------

    private static bool TryImage(string? dataUri, out byte[] png)
    {
        png = [];
        if (string.IsNullOrWhiteSpace(dataUri)) return false;
        var comma = dataUri.IndexOf(',');
        if (comma < 0 || !dataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return false;
        try { png = Convert.FromBase64String(dataUri[(comma + 1)..]); return png.Length > 0; }
        catch { return false; }
    }

    private static byte[]? BuildQr(OrderDto order, StoreProfile store, ReceiptDesignDto design)
    {
        try
        {
            var link = design.QrMode switch
            {
                "link" when !string.IsNullOrWhiteSpace(design.QrLink) => design.QrLink!,
                "store" => $"{store.ClientUrl.TrimEnd('/')}/restaurant/{store.Id}",
                _ => BillCode.LinkFor(order.Id, store.ClientUrl),
            };
            var dots = design.QrSize switch { "small" => 3, "large" => 6, _ => 4 };
            using var generator = new QRCoder.QRCodeGenerator();
            using var data = generator.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
            return new QRCoder.PngByteQRCode(data).GetGraphic(dots);
        }
        catch { return null; }
    }

    private static string QrCaption(ReceiptDesignDto d) => !string.IsNullOrWhiteSpace(d.QrCaption)
        ? d.QrCaption!
        : d.QrMode == "verify" ? "Scan to verify this bill" : "Scan me";

    // ---------- text helpers ----------

    private static string Amount(decimal value)
    {
        var text = value.ToString("0.000", CultureInfo.InvariantCulture);
        if (!text.Contains('.')) return text;
        text = text.TrimEnd('0').TrimEnd('.');
        return text.Length == 0 || text == "-" ? "0" : text;
    }

    private static string Money(decimal value) => $"{Amount(value)} OMR";

    private static bool IsDineIn(OrderDto o) => o.TableName is { Length: > 0 }
        || o.DeliveryAddress?.StartsWith("Dine-in", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsCounter(OrderDto o) => !IsDineIn(o) && o.DeliveryAddress?.Equals("Counter", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsDelivery(OrderDto o) => !IsDineIn(o) && !IsCounter(o) && o.OrderType == OrderType.Delivery;

    private static string TypeWord(OrderDto o, bool bilingual)
    {
        var (en, ar) = IsDineIn(o) ? ("DINE-IN", "محلي")
            : IsCounter(o) ? ("COUNTER", "كاونتر")
            : IsDelivery(o) ? ("DELIVERY", "توصيل")
            : ("PICKUP", "استلام");
        return bilingual ? $"{en} / {ar}" : en;
    }

    private static string PayWord(OrderDto o) => o.PaymentMethod switch
    {
        PaymentMethod.CardOnline => "ONLINE",
        PaymentMethod.CardOnDelivery => "CARD",
        _ => "CASH",
    };

    // ---------- preview text layout ----------

    private static string Place(string text, Align align, int width)
    {
        if (text.Length >= width) return text[..width];
        return align switch
        {
            Align.Centre => text.PadLeft((width + text.Length) / 2).PadRight(width),
            Align.Right => text.PadLeft(width),
            _ => text,
        };
    }

    private static string Justify(string left, string right, int width)
    {
        if (left.Length + right.Length + 1 > width)
            left = left[..Math.Max(0, width - right.Length - 2)] + "…";
        return left.PadRight(width - right.Length) + right;
    }
}

/// <summary>
/// The handful of store facts a printed receipt needs, pulled from the partner API's
/// <c>MyRestaurantDto</c> plus the client host the QR should point at.
/// </summary>
public sealed record StoreProfile(
    int Id, string Name, string Area, string Street, string Phone,
    string CrNumber, string VatNumber, string ClientUrl)
{
    public static StoreProfile From(MyRestaurantDto r, string clientUrl) =>
        new(r.Id, r.Name, r.Area, r.Street, r.Phone, r.CrNumber, r.VatNumber, clientUrl);

    public static StoreProfile Fallback(string name, string clientUrl) =>
        new(0, name, "", "", "", "", "", clientUrl);
}
