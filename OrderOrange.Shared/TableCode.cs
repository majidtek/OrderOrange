using System.Security.Cryptography;
using System.Text;

namespace OrderOrange.Shared;

/// <summary>
/// The signed code a table's QR carries: <c>storeId-tableId-signature</c>. Whoever
/// scans it lands on the public reservation page with no account — the signature is
/// what stops anyone typing random ids and reserving tables at stores they invented.
/// </summary>
public static class TableCode
{
    private static byte[] _key = Encoding.UTF8.GetBytes("wajibat-bill-verify-v1");

    /// <summary>Point every app at the same secret at startup (same one the bills use).</summary>
    public static void UseSecret(string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret)) _key = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>The code printed inside a table's QR.</summary>
    public static string For(int storeId, int tableId) => $"{storeId}-{tableId}-{Sign(storeId, tableId)}";

    /// <summary>The full URL the QR encodes.</summary>
    public static string LinkFor(int storeId, int tableId, string clientBaseUrl) =>
        $"{clientBaseUrl.TrimEnd('/')}/reserve/{For(storeId, tableId)}";

    /// <summary>Reads a scanned code, rejecting anything not signed by us.</summary>
    public static bool TryRead(string? code, out int storeId, out int tableId)
    {
        storeId = 0;
        tableId = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;
        var parts = code.Split('-');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var s) || s <= 0) return false;
        if (!int.TryParse(parts[1], out var t) || t <= 0) return false;

        var expected = Sign(s, t);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(parts[2]), Encoding.ASCII.GetBytes(expected)))
            return false;

        storeId = s;
        tableId = t;
        return true;
    }

    private static string Sign(int storeId, int tableId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"table:{storeId}:{tableId}"));
        return Convert.ToHexString(mac)[..12].ToLowerInvariant();
    }
}
