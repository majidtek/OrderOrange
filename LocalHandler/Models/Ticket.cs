namespace LocalHandler.Models;

/// <summary>One line's quantity before and after an edit of an open invoice. From 0 = a
/// line added to an invoice the kitchen already has; To 0 = the line was struck.</summary>
public sealed record LineChange(int From, int To)
{
    public bool IsNew => From == 0;
    public bool IsRemoved => To == 0;
    public int Delta => To - From;
}

/// <summary>What the kitchen was last told about an open-invoice line: which tab it sits
/// on, how many, and its name/price (kept so a struck line can still be printed).</summary>
public sealed record TabLineMemo(int TabId, int Qty, string Name, decimal UnitPrice);

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
