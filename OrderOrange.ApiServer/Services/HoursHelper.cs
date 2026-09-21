using OrderOrange.ApiServer.Models;

namespace OrderOrange.ApiServer.Services;

public static class HoursHelper
{
    /// <summary>
    /// Is the restaurant inside today's working window? No configured rows = always
    /// (the manual open/closed switch is the only gate then). Handles overnight
    /// windows where Close &lt; Open (e.g. 18:00–02:00).
    /// </summary>
    public static bool IsWithinHours(IReadOnlyCollection<RestaurantHours> hours, DateTime now)
    {
        if (hours.Count == 0) return true;

        var today = hours.FirstOrDefault(h => h.Day == (int)now.DayOfWeek);
        var time = now.TimeOfDay;

        // A window that started yesterday evening can still be running past midnight.
        var yesterday = hours.FirstOrDefault(h => h.Day == (int)now.AddDays(-1).DayOfWeek);
        if (yesterday is { IsClosed: false } && yesterday.Close < yesterday.Open && time < yesterday.Close)
            return true;

        if (today is null || today.IsClosed) return false;
        return today.Close < today.Open
            ? time >= today.Open || time < today.Close   // overnight
            : time >= today.Open && time < today.Close;  // same-day
    }

    /// <summary>"10:00–23:00", "Closed today", or null when no schedule is configured.</summary>
    public static string? TodayLabel(IReadOnlyCollection<RestaurantHours> hours, DateTime now)
    {
        if (hours.Count == 0) return null;
        var today = hours.FirstOrDefault(h => h.Day == (int)now.DayOfWeek);
        if (today is null || today.IsClosed) return "Closed today";
        return $"{today.Open:hh\\:mm}–{today.Close:hh\\:mm}";
    }
}

/// <summary>Tiny fuzzy matcher so search survives typos ("piza", "burgr", "شاورمة").</summary>
public static class Fuzzy
{
    public static bool Matches(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query)) return false;
        var queryTokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var textTokens = text.ToLowerInvariant().Split([' ', ',', '&', '-'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var q in queryTokens)
        {
            var threshold = q.Length <= 3 ? 0 : q.Length <= 5 ? 1 : q.Length <= 8 ? 2 : 3;
            var hit = textTokens.Any(t =>
                t.StartsWith(q, StringComparison.Ordinal) ||
                q.StartsWith(t, StringComparison.Ordinal) ||
                Distance(t, q, threshold) <= threshold);
            if (!hit) return false; // every query word must roughly match somewhere
        }
        return true;
    }

    /// <summary>Levenshtein distance with an early-exit cap.</summary>
    private static int Distance(string a, string b, int cap)
    {
        if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var best = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                best = Math.Min(best, current[j]);
            }
            if (best > cap) return cap + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
