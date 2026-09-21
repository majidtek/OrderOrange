using System.Security.Cryptography;
using System.Text;

namespace OrderOrange.Shared;

/// <summary>
/// A small signed pass the SERVER hands to its own pages, and later demands back
/// on the map/search endpoints. Outsiders hitting those URLs cold have no pass —
/// so they cannot spend the Google quota — while every real page (including the
/// anonymous registration wizard) gets one for free when it renders.
/// </summary>
public static class FormToken
{
    private static readonly byte[] Key =
        SHA256.HashData(Encoding.UTF8.GetBytes("orderorange-form-gate-v1-2026"));

    public static string Create()
    {
        var ticks = DateTime.UtcNow.Ticks.ToString();
        return $"{ticks}.{Sign(ticks)}";
    }

    public static bool Validate(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var dot = token.IndexOf('.');
        if (dot <= 0) return false;
        var ticksPart = token[..dot];
        if (!long.TryParse(ticksPart, out var ticks)) return false;

        // A pass is good for six hours — long enough for the slowest form,
        // far too short to be worth harvesting and sharing.
        var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        if (age < TimeSpan.FromMinutes(-5) || age > TimeSpan.FromHours(6)) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token[(dot + 1)..]),
            Encoding.UTF8.GetBytes(Sign(ticksPart)));
    }

    private static string Sign(string payload) =>
        Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload)))[..24].ToLowerInvariant();
}
