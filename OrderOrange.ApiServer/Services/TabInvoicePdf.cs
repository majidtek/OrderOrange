using OrderOrange.Shared;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The guest's invoice as a PDF, drawn directly — no browser involved. The old route
/// fed the public invoice page to a headless Chrome, which on this box took 20–35
/// seconds per file (Chrome start + a Blazor circuit) and left the guest staring at
/// a blank tab (Majed 2026-09-07: "download invoice pdf not working, no progress").
/// This renders the same slip in well under a second: an 80 mm receipt, logo, lines,
/// totals, the table's QR — bilingual labels, Arabic and Farsi shaped by the font.
/// </summary>
public static class TabInvoicePdf
{
    private static bool _configured;

    private static void Configure()
    {
        if (_configured) return;
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
        _configured = true;
    }

    public static byte[] Render(PublicTabInvoiceDto inv, int tabId) => Build(inv, tabId).GeneratePdf();

    /// <summary>The first page as a PNG — for a quick look, or to share in a chat.</summary>
    public static byte[] RenderPng(PublicTabInvoiceDto inv, int tabId)
    {
        var images = Build(inv, tabId).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 144,
        });
        return images.First();
    }

    private static IDocument Build(PublicTabInvoiceDto inv, int tabId)
    {
        Configure();
        var d = inv.Design ?? ReceiptDesignDto.Default;
        var bilingual = d.LabelStyle == "bilingual";
        string Lb(string en, string ar) => bilingual ? $"{en} · {ar}" : en;
        var logo = DecodeDataUri(d.Logo is { Length: > 0 } ? d.Logo : inv.LogoData);
        var qr = DecodeDataUri(inv.QrDataUrl);
        var arabicName = inv.StoreNames is not null && inv.StoreNames.TryGetValue("ar", out var ar) && ar.Length > 0 && ar != inv.StoreName ? ar : null;
        var faName = inv.StoreNames is not null && inv.StoreNames.TryGetValue("fa", out var fa) && fa.Length > 0 && fa != inv.StoreName && fa != arabicName ? fa : null;

        // The store's slip fonts map onto real faces; every one of them must carry Arabic.
        var family = d.Font switch { "mono" => "Consolas", "serif" => "Times New Roman", _ => "Segoe UI" };
        var baseSize = d.FontSize switch { "small" => 8.5f, "large" => 11f, _ => 9.5f };
        var text = TextStyle.Default.FontFamily(family, "Tahoma", "Arial")
            .FontSize(baseSize).FontColor(Colors.Black).DirectionAuto();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.ContinuousSize(d.PaperWidth == 58 ? 58 : 80, Unit.Millimetre);
                page.Margin(4, Unit.Millimetre);
                page.DefaultTextStyle(text);
                page.Content().Column(col =>
                {
                    col.Spacing(2);

                    // The mark leads, the name follows quietly ("add more size to logo and title
                    // less font size" — Majed 2026-09-07).
                    if (logo is not null)
                        col.Item().AlignCenter().PaddingBottom(1.5f, Unit.Millimetre)
                            .MaxHeight(d.LogoSize == "large" ? 38 : d.LogoSize == "small" ? 20 : 30, Unit.Millimetre)
                            .Image(logo).FitHeight();

                    col.Item().AlignCenter().Text(inv.StoreName).FontSize(baseSize + 1.5f).Bold();
                    if (arabicName is not null) col.Item().AlignCenter().Text(arabicName).FontSize(baseSize + 0.5f).Bold();
                    if (faName is not null) col.Item().AlignCenter().Text(faName).FontSize(baseSize);
                    if (!string.IsNullOrWhiteSpace(d.HeaderMessage)) col.Item().AlignCenter().Text(d.HeaderMessage).Italic();
                    if (d.ShowAddress)
                    {
                        var where = string.Join(" · ", new[] { inv.Area, inv.Street }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (where.Length > 0) col.Item().AlignCenter().Text(where).FontSize(baseSize - 1);
                        col.Item().AlignCenter().Text($"Tel: {inv.Phone} · Muscat, Oman").FontSize(baseSize - 1);
                    }
                    if (d.ShowVat && (inv.CrNumber.Length > 0 || inv.VatNumber.Length > 0))
                    {
                        var ids = string.Join(" · ", new[] {
                            inv.CrNumber.Length > 0 ? $"CR No: {inv.CrNumber}" : "",
                            inv.VatNumber.Length > 0 ? $"VAT No: {inv.VatNumber}" : "" }.Where(s => s.Length > 0));
                        col.Item().AlignCenter().Text(ids).FontSize(baseSize - 2);
                    }

                    Sep(col, d);
                    col.Item().AlignCenter().Text(bilingual ? "TAX INVOICE — فاتورة" : "TAX INVOICE").FontSize(baseSize + 1).Bold();
                    Row(col, Lb("Receipt #", "رقم الإيصال"), tabId.ToString("D6"));
                    Row(col, Lb("Date", "التاريخ"), inv.OpenedAt.ToString("dd/MM/yyyy HH:mm"));
                    Row(col, Lb("Type", "النوع"), Lb("DINE-IN", "محلي"));
                    Row(col, Lb("Table", "الطاولة"), inv.TableName, bold: true);
                    Sep(col, d);

                    // Lines: item, qty, unit price, amount.
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(5); c.ConstantColumn(22); c.RelativeColumn(2); c.RelativeColumn(2); });
                        t.Header(h =>
                        {
                            h.Cell().Text(Lb("ITEM", "الصنف")).Bold().FontSize(baseSize - 1);
                            h.Cell().AlignCenter().Text(Lb("QTY", "كمية")).Bold().FontSize(baseSize - 1);
                            h.Cell().AlignRight().Text(Lb("PRICE", "السعر")).Bold().FontSize(baseSize - 1);
                            h.Cell().AlignRight().Text(Lb("AMT", "المبلغ")).Bold().FontSize(baseSize - 1);
                        });
                        foreach (var line in inv.Lines)
                        {
                            t.Cell().PaddingVertical(1).Text(line.Name);
                            t.Cell().PaddingVertical(1).AlignCenter().Text(line.Quantity.ToString());
                            t.Cell().PaddingVertical(1).AlignRight().Text(line.UnitPrice.ToString("0.000"));
                            t.Cell().PaddingVertical(1).AlignRight().Text((line.UnitPrice * line.Quantity).ToString("0.000"));
                        }
                    });
                    Sep(col, d);

                    Row(col, Lb("Items", "العناصر"), inv.Lines.Sum(l => l.Quantity).ToString());
                    Row(col, Lb("Subtotal", "المجموع"), Rial(inv.Subtotal));
                    if (inv.Tax > 0) Row(col, Lb($"VAT {inv.TaxPercent:0.#}%", $"الضريبة {inv.TaxPercent:0.#}٪"), Rial(inv.Tax));
                    Sep(col, d);

                    if (d.TotalStyle == "invert")
                        col.Item().Background(Colors.Black).Padding(3).Row(r =>
                        {
                            r.RelativeItem().Text(Lb("TOTAL", "الإجمالي")).FontColor(Colors.White).Bold().FontSize(baseSize + 2);
                            r.AutoItem().Text(Rial(inv.Total)).FontColor(Colors.White).Bold().FontSize(baseSize + 2);
                        });
                    else
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(Lb("TOTAL", "الإجمالي")).Bold().FontSize(baseSize + 2);
                            r.AutoItem().Text(Rial(inv.Total)).Bold().FontSize(baseSize + 2);
                        });
                    col.Item().AlignCenter().PaddingTop(2).Text($"*** TO PAY — {inv.Total:0.000} OMR ***").Bold();

                    if (qr is not null)
                    {
                        Sep(col, d);
                        col.Item().AlignCenter().Width(d.QrSize == "large" ? 30 : d.QrSize == "small" ? 16 : 22, Unit.Millimetre).Image(qr).FitWidth();
                        col.Item().AlignCenter().Text(Lb("Scan to order again", "امسح للطلب مرة أخرى")).FontSize(baseSize - 2);
                    }
                    Sep(col, d);
                    col.Item().AlignCenter().Text(string.IsNullOrWhiteSpace(d.FooterMessage) ? "Thank you — see you soon!" : d.FooterMessage).FontSize(baseSize - 1);
                    if (!string.IsNullOrWhiteSpace(d.Promo)) col.Item().AlignCenter().Text(d.Promo).FontSize(baseSize - 1).Italic();
                    col.Item().AlignCenter().Text($"Printed {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(baseSize - 2.5f).FontColor(Colors.Grey.Darken1);
                });
            });
        });
    }

    private static void Row(ColumnDescriptor col, string label, string value, bool bold = false) =>
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(label);
            var v = r.AutoItem().Text(value);
            if (bold) v.Bold();
        });

    /// <summary>The slip's separator, in the style the store chose for its paper.</summary>
    private static void Sep(ColumnDescriptor col, ReceiptDesignDto d)
    {
        // Drawn lines, not typed dashes: a string of "- -" wrapped onto a second row at
        // 80 mm in the mono face. Each style keeps its own weight so the slip still reads.
        var item = col.Item().PaddingVertical(2f);
        switch (d.Separator)
        {
            case "solid": item.LineHorizontal(0.8f).LineColor(Colors.Black); break;
            case "dots": item.LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1); break;
            case "stars": item.LineHorizontal(1.2f).LineColor(Colors.Grey.Darken2); break;
            default: item.LineHorizontal(0.6f).LineColor(Colors.Grey.Medium); break;
        }
    }

    private static string Rial(decimal amount) => $"{amount:0.000} OMR";

    private static byte[]? DecodeDataUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        var comma = uri.IndexOf(',');
        if (comma < 0 || !uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return null;
        try { return Convert.FromBase64String(uri[(comma + 1)..]); }
        catch (FormatException) { return null; }
    }
}
