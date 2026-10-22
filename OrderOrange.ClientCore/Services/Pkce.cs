using System.Security.Cryptography;
using System.Text;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// The app's Google sign-in runs in the device browser and comes back with a one-time code.
/// The code is bound to a challenge the app made from a secret it keeps (PKCE), so a code
/// seen by anything else on the way is worthless without that secret.
/// </summary>
public static class Pkce
{
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>One parameter out of an address's query string, decoded; null when absent.</summary>
public static class UrlQuery
{
    public static string? Get(string uri, string name)
    {
        var q = new Uri(uri).Query.TrimStart('?');
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
            return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }
        return null;
    }
}
