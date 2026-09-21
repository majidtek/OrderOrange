using OrderOrange.Shared;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace OrderOrange.ClientCore.Services;

/// <summary>Display helpers shared by all four apps, so money and statuses look identical everywhere.</summary>
public static class Fmt
{
    /// <summary>The rial, and what every shop shows until it says otherwise.</summary>
    public const string DefaultSymbol = "OMR";

    /// <summary>
    /// What money is called on this screen — "OMR", "﷼", "$", "د.إ". The shop picks it in
    /// Settings and the whole portal follows.
    ///
    /// A plain static is right HERE and only here: the partner and admin apps are
    /// WebAssembly, one browser per person, so this belongs to that one signed-in shop.
    /// The customer site is Blazor Server, where a static would be shared by every visitor
    /// at once — it leaves this on the default and never assigns it.
    /// </summary>
    public static string Symbol { get; set; } = DefaultSymbol;

    /// <summary>The Central Bank's glyph stands in only when the money really is rials.</summary>
    public static bool UsesRialGlyph => Symbol is DefaultSymbol or "OMR" or "ر.ع." or "﷼";

    public static string Money(decimal value) => $"{Amount(value)} {Symbol}";

    // ---------- The official rial symbol (Central Bank of Oman, Nov 2025) ----------
    // Inline SVG of the CBO glyph, wearing currentColor so it always matches the
    // text beside it. Unicode gives it U+20C4 in v18 (Sep 2026); until fonts ship
    // that glyph everywhere, the vector IS the symbol.
    public const string RialSvg =
        "<svg class=\"omr-i\" viewBox=\"0 0 922 480\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"OMR\">" +
        "<path fill=\"currentColor\" d=\"M922.01,236.09l-59.34,95.95-333.24-.07c25.34,15.62,52.07,30.01,80.14,40.23,7.12,2.58,30.02,10.71,36.24,10.71h179.3l-56.94,96.38c-12.64.7-651.61-.33-755.19-.49-5.78,0-9.89,0-12.13,0h-.85l55.44-95.88h343.63c.12,0-26.38-33.78-38.46-50.94l-264.7-.49,56.43-95.39h173.31c-.8-61.59,16.04-121.33,49.53-172.7,31.83-48.8,63.2-75.17,125.05-58.28,41.3,11.29,78.52,39.73,107.65,70.21l-36.5,142.79c-1.77.36-12.58-12.88-16.95-17.5-37.63-39.81-102.37-90.88-160.53-66.57-14.35,6-29.47,19.14-30.02,35.91-.73,22.61,27.02,49.43,40.69,65.64l517.44.5Z\"/></svg>";

    /// <summary>
    /// Money wearing the official rial glyph — or, for a shop that chose another currency,
    /// its own symbol. The drawn glyph exists because no font ships U+20C4 yet; a dollar
    /// sign needs no such help.
    /// </summary>
    public static MarkupString Rial(decimal value) =>
        new(UsesRialGlyph
            ? $"{Amount(value)} {RialSvg}"
            : $"{Amount(value)} {System.Net.WebUtility.HtmlEncode(Symbol)}");

    /// <summary>MoneyShort's figure, wearing the symbol.</summary>
    public static MarkupString RialShort(decimal value)
    {
        var figure = MoneyShort(value);
        if (figure.EndsWith(" " + Symbol)) figure = figure[..^(Symbol.Length + 1)];
        return new(UsesRialGlyph
            ? $"{figure} {RialSvg}"
            : $"{figure} {System.Net.WebUtility.HtmlEncode(Symbol)}");
    }

    /// <summary>
    /// The figure alone, for lists where the currency is already stated once (chat cart
    /// rows) — repeating "OMR" on every line squeezed the product name out of the row.
    ///
    /// Rials carry three decimals (baisa), but only where they say something: a round
    /// price reads "1", not "1.000", and 1.500 keeps the half it needs. Trailing zeros
    /// are noise that makes every figure look like a machine dump.
    /// </summary>
    public static string Amount(decimal value)
    {
        var text = value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        if (!text.Contains('.')) return text;
        text = text.TrimEnd('0').TrimEnd('.');
        return text.Length == 0 || text == "-" ? "0" : text;
    }

    /// <summary>
    /// Money for narrow places (stat tiles, table cells): big figures drop the
    /// fractions and group thousands, so "17089.801 OMR" reads "17,090 OMR"
    /// and never wraps onto a second line.
    /// </summary>
    public static string MoneyShort(decimal value)
    {
        var abs = Math.Abs(value);
        if (abs >= 1_000_000) return $"{value / 1_000_000m:0.#}M {Symbol}";
        if (abs >= 1_000) return $"{value:#,##0} {Symbol}";
        return $"{Amount(value)} {Symbol}";
    }


    // ---------- Localized variants: pass the page's LanguageService ----------

    /// <summary>"just now" / "5 min ago" / "3 h ago" in the user's language.</summary>
    public static string TimeAgo(DateTime at, LanguageService lang)
    {
        var span = DateTime.Now - at;
        return span.TotalMinutes < 1 ? lang["time.justNow"]
            : span.TotalMinutes < 60 ? string.Format(lang["time.minAgo"], (int)span.TotalMinutes)
            : span.TotalHours < 24 ? string.Format(lang["time.hAgo"], (int)span.TotalHours)
            : at.ToString("dd MMM, HH:mm");
    }

    public static string StatusLabel(OrderStatus status, LanguageService lang) => lang[$"status.{status}"];

    public static string VehicleLabel(VehicleType vehicle, LanguageService lang) => lang[$"vehicle.{vehicle}"];

    public static string PaymentLabel(PaymentMethod payment, LanguageService lang) => lang[$"pay.{payment}"];

    public static string RoleLabel(UserRole role, LanguageService lang) => lang[$"role.{role}"];


    public static Color StatusColor(OrderStatus status) => status switch
    {
        OrderStatus.Pending => Color.Warning,
        OrderStatus.Accepted or OrderStatus.Preparing => Color.Info,
        OrderStatus.Ready or OrderStatus.PickedUp or OrderStatus.OnTheWay => Color.Primary,
        OrderStatus.Delivered => Color.Success,
        _ => Color.Error
    };

    public static string StatusIcon(OrderStatus status) => status switch
    {
        OrderStatus.Pending => Icons.Material.Filled.HourglassTop,
        OrderStatus.Accepted => Icons.Material.Filled.ThumbUp,
        OrderStatus.Preparing => Icons.Material.Filled.SoupKitchen,
        OrderStatus.Ready => Icons.Material.Filled.TakeoutDining,
        OrderStatus.PickedUp => Icons.Material.Filled.DeliveryDining,
        OrderStatus.OnTheWay => Icons.Material.Filled.Moped,
        OrderStatus.Delivered => Icons.Material.Filled.CheckCircle,
        OrderStatus.Cancelled => Icons.Material.Filled.Cancel,
        OrderStatus.Rejected => Icons.Material.Filled.Block,
        _ => Icons.Material.Filled.Circle
    };



}
