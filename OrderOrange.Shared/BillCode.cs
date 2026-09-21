using System.Security.Cryptography;
using System.Text;

namespace OrderOrange.Shared;

/// <summary>
/// The token printed on a receipt's verification QR: "&lt;orderId&gt;-&lt;signature&gt;".
/// The signature keeps order ids from being guessable, so the bill page can be public —
/// only someone holding the printed receipt can open it.
/// </summary>
public static class BillCode
{
    // Deliberately keeps its pre-rename value. Receipts already printed carry QR codes
    // signed with this secret; changing it would stop every one of them verifying.
    private static byte[] _key = Encoding.UTF8.GetBytes("wajibat-bill-verify-v1");

    /// <summary>Point every app at the same secret at startup (falls back to a shared default).</summary>
    public static void UseSecret(string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret)) _key = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>The code to print for an order.</summary>
    public static string For(int orderId) => $"{orderId}-{Sign(orderId)}";

    /// <summary>The full URL the QR encodes.</summary>
    public static string LinkFor(int orderId, string clientBaseUrl) =>
        $"{clientBaseUrl.TrimEnd('/')}/bill/{For(orderId)}";

    /// <summary>Reads a scanned code back to an order id, rejecting anything not signed by us.</summary>
    public static bool TryRead(string? code, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;
        var dash = code.IndexOf('-');
        if (dash <= 0 || dash == code.Length - 1) return false;
        if (!int.TryParse(code[..dash], out var id) || id <= 0) return false;

        var given = code[(dash + 1)..];
        var expected = Sign(id);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(given.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected)))
            return false;

        orderId = id;
        return true;
    }

    private static string Sign(int orderId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"bill:{orderId}"));
        return Convert.ToHexString(mac)[..10].ToLowerInvariant();
    }
}
