using System.Globalization;
using System.Text.RegularExpressions;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Digs coordinates out of a Google Maps link. Full links are pure regex; the short
/// goo.gl ones are followed server-side to their long form first — and ONLY Google
/// hosts are ever followed, so this can never become a generic URL fetcher.
/// </summary>
public static class MapLinkResolver
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    { Timeout = TimeSpan.FromSeconds(8) };

    public static bool IsGoogleHost(string host) =>
        host.Equals("goo.gl", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".goo.gl", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("google.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase) ||
        host.StartsWith("maps.google.", StringComparison.OrdinalIgnoreCase) ||
        host.StartsWith("www.google.", StringComparison.OrdinalIgnoreCase);

    /// <summary>The shapes Google hides coordinates in: @lat,lng · q=/ll= pairs · !3d…!4d….</summary>
    public static (double Lat, double Lng)? TryParse(string text)
    {
        var m = Regex.Match(text, @"@(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)");
        if (!m.Success) m = Regex.Match(text, @"[?&](?:q|ll|query|destination)=(-?\d{1,2}\.\d+)(?:,|%2C)(-?\d{1,3}\.\d+)");
        if (!m.Success) m = Regex.Match(text, @"!3d(-?\d{1,2}\.\d+)!4d(-?\d{1,3}\.\d+)");
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(m.Groups[2].Value, CultureInfo.InvariantCulture, out var lng))
            return null;
        return lat is < -90 or > 90 || lng is < -180 or > 180 ? null : (lat, lng);
    }

    /// <summary>
    /// Place search through Google's official Places Text Search API, biased to Oman.
    /// No key configured = an empty answer, never an error in the customer's face.
    /// </summary>
    public static async Task<List<(string Name, string Address, double Lat, double Lng)>> SearchPlacesAsync(string query, string? lang, string? apiKey)
    {
        var results = new List<(string, string, double, double)>();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query) || query.Length > 120)
            return results;
        try
        {
            using var response = await Http.GetAsync(
                "https://maps.googleapis.com/maps/api/place/textsearch/json" +
                $"?query={Uri.EscapeDataString(query.Trim())}" +
                "&region=om" +
                $"&language={Uri.EscapeDataString(string.IsNullOrWhiteSpace(lang) ? "en" : lang)}" +
                $"&key={Uri.EscapeDataString(apiKey)}");
            if (!response.IsSuccessStatusCode) return results;
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("results", out var hits)) return results;
            foreach (var hit in hits.EnumerateArray().Take(5))
            {
                var name = hit.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var address = hit.TryGetProperty("formatted_address", out var a) ? a.GetString() ?? "" : "";
                var location = hit.GetProperty("geometry").GetProperty("location");
                var lat = location.GetProperty("lat").GetDouble();
                var lng = location.GetProperty("lng").GetDouble();
                results.Add((name, address, lat, lng));
            }
        }
        catch { /* a silent search box beats an error box */ }
        return results;
    }

    public static async Task<(double Lat, double Lng)?> ResolveAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || !IsGoogleHost(uri.Host))
            return null;

        var direct = TryParse(url);
        if (direct is not null) return direct;

        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            var final = response.RequestMessage?.RequestUri?.ToString() ?? "";
            var point = TryParse(final);
            if (point is null && response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                point = TryParse(body[..Math.Min(body.Length, 200_000)]);
            }
            return point;
        }
        catch
        {
            return null;
        }
    }
}
