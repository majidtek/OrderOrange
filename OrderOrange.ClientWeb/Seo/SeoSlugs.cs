using System.Text;
using System.Text.RegularExpressions;

namespace OrderOrange.ClientWeb.Seo;

/// <summary>
/// The addresses of the search landing pages — one per cuisine and one per area — and the
/// slugs they use. Kept in one place so the page, the head, the footer and the sitemap can
/// never disagree about what <c>/cuisines/arabic-grill</c> means.
/// </summary>
public static class SeoSlugs
{
    public const string Hub = "/food-delivery";

    /// <summary>The other half of the product: OrderOrange as a till, not a menu.</summary>
    public const string Pos = "/pos";

    /// <summary>"Arabic &amp; Grill" → "arabic-grill", "Al Khuwair" → "al-khuwair".</summary>
    public static string Slug(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text.Trim().ToLowerInvariant().Replace("&", " and ");
        s = Regex.Replace(s, @"[^a-z0-9؀-ۿ]+", "-").Trim('-');
        return s.Length > 60 ? s[..60].Trim('-') : s;
    }

    public static string CuisinePath(string cuisine) => "/cuisines/" + Slug(cuisine);
    public static string AreaPath(string area) => "/areas/" + Slug(area);

    /// <summary>Areas worth a page: named, and carrying at least one listed store.</summary>
    public static IEnumerable<string> Areas(IEnumerable<string?> areas) =>
        areas.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim())
             .GroupBy(a => Slug(a)).Where(g => g.Key.Length > 1).Select(g => g.First()).OrderBy(a => a);
}
