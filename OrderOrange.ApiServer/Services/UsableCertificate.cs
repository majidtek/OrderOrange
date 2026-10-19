using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Picks the certificate the public HTTPS endpoints serve, from the store and subject the
/// Kestrel config names — but only one whose PRIVATE KEY this process can actually use.
///
/// <para>Why (found 2026-10-13): win-acme renewed the orderorange.com SAN certificate on
/// 2026-10-06 with a key the API's account may not read ("Keyset does not exist"). Kestrel
/// takes the newest certificate by subject, so after the next restart every TLS handshake on
/// the public ports (8500 till, staging 8600 and 8443 — the port xAI uses to reach the staging
/// MCP server) ended in an empty reply. Here the newest certificate with a USABLE key wins,
/// and the choice is re-checked every 30 minutes so a renewal with a proper key is picked up
/// without a restart. Proper fix for the renewal itself: give the account read access to
/// the new key (an admin task, outside this code).</para>
/// </summary>
public static class UsableCertificate
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (X509Certificate2? Cert, DateTime At)> Cache = [];

    /// <summary>The store is given by NAME so IIS's "WebHosting" store works (it is not a StoreName value).</summary>
    public static X509Certificate2? Get(string subject, string store, StoreLocation location)
    {
        var key = $"{location}/{store}/{subject}";
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var hit) && hit.Cert is not null && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(30))
                return hit.Cert;
            var chosen = Pick(subject, store, location);
            Cache[key] = (chosen ?? hit.Cert, DateTime.UtcNow);
            return chosen ?? hit.Cert;
        }
    }

    private static X509Certificate2? Pick(string subject, string store, StoreLocation location)
    {
        try
        {
            using var s = new X509Store(store, location);
            s.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var now = DateTime.Now;
            return s.Certificates
                .Find(X509FindType.FindBySubjectName, subject, validOnly: false)
                // FindBySubjectName is a substring match ("orderorange.com" also finds
                // api.orderorange.com) — keep only the exact common name.
                .Where(c => string.Equals(c.GetNameInfo(X509NameType.SimpleName, false), subject, StringComparison.OrdinalIgnoreCase))
                .Where(c => c.HasPrivateKey && c.NotBefore <= now && c.NotAfter > now)
                .OrderByDescending(c => c.NotAfter)
                .FirstOrDefault(KeyUsable);
        }
        catch { return null; }
    }

    private static bool KeyUsable(X509Certificate2 c)
    {
        try
        {
            using var rsa = c.GetRSAPrivateKey();
            if (rsa is not null) { rsa.SignData([1, 2, 3], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); return true; }
            using var ec = c.GetECDsaPrivateKey();
            if (ec is not null) { ec.SignData([1, 2, 3], HashAlgorithmName.SHA256); return true; }
        }
        catch { /* "Keyset does not exist" — not usable by this account */ }
        return false;
    }
}
