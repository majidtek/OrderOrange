using System.Security.Cryptography;
using System.Text;

namespace OrderOrange.Shared;

/// <summary>
/// The token in a contract's public link: "&lt;contractId&gt;-&lt;signature&gt;". Signed the
/// same way as <see cref="BillCode"/> so the page can be public — the customer opens
/// their agreement from any browser, but nobody can walk the ids and read other
/// people's terms.
/// </summary>
public static class ContractCode
{
    private static byte[] _key = Encoding.UTF8.GetBytes("wajibat-bill-verify-v1");

    /// <summary>Point every app at the same secret at startup (falls back to a shared default).</summary>
    public static void UseSecret(string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret)) _key = Encoding.UTF8.GetBytes(secret);
    }

    public static string For(int contractId) => $"{contractId}-{Sign(contractId)}";

    /// <summary>The full URL the email carries.</summary>
    public static string LinkFor(int contractId, string clientBaseUrl) =>
        $"{clientBaseUrl.TrimEnd('/')}/contract/{For(contractId)}";

    public static bool TryRead(string? code, out int contractId)
    {
        contractId = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;
        var dash = code.IndexOf('-');
        if (dash <= 0 || dash == code.Length - 1) return false;
        if (!int.TryParse(code[..dash], out var id) || id <= 0) return false;

        var given = code[(dash + 1)..];
        var expected = Sign(id);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(given.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected)))
            return false;

        contractId = id;
        return true;
    }

    private static string Sign(int contractId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"contract:{contractId}"));
        return Convert.ToHexString(mac)[..10].ToLowerInvariant();
    }
}
