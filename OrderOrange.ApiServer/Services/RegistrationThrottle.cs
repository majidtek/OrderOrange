using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Caps how many accounts one network address may create per day. Without it, a script
/// can mint accounts without limit — fake partners, review farms, coupon abuse.
///
/// The address is taken from the connection, EXCEPT when the call comes from loopback:
/// the four web apps run on this same machine and relay for their visitors, so a
/// loopback caller's X-Client-IP header is honoured. An outside caller's header is
/// ignored — you cannot spoof your way past the limit from the internet.
/// </summary>
public sealed class RegistrationThrottle(IMemoryCache cache, IConfiguration config)
{
    private int DailyLimit =>
        int.TryParse(config["Auth:MaxRegistrationsPerIpPerDay"], out var n) && n > 0 ? n : 20;

    public static string ClientIp(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        // Through Cloudflare + the api.orderorange.com proxy the socket is loopback and
        // X-Forwarded-For is Cloudflare's edge; the visitor is in CF-Connecting-IP.
        if (remote is not null && IPAddress.IsLoopback(remote) &&
            context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cf) &&
            cf.Count > 0 && !string.IsNullOrWhiteSpace(cf[0]))
        {
            return cf[0]!.Trim();
        }
        if (remote is not null && IPAddress.IsLoopback(remote) &&
            context.Request.Headers.TryGetValue("X-Client-IP", out var forwarded) &&
            forwarded.Count > 0 && !string.IsNullOrWhiteSpace(forwarded[0]))
        {
            return forwarded[0]!.Split(',')[0].Trim();
        }
        return remote?.ToString() ?? "unknown";
    }

    /// <summary>True if this address may create another account today; counts the attempt.</summary>
    public bool TryRegister(HttpContext context)
    {
        var key = $"regcount:{ClientIp(context)}:{DateTime.UtcNow:yyyyMMdd}";
        var count = cache.TryGetValue(key, out object? raw) && raw is int n ? n : 0;
        if (count >= DailyLimit) return false;

        // 25h, not 24: the window is per calendar day; the extra hour just guarantees
        // the entry outlives its own day before eviction.
        cache.Set(key, count + 1, TimeSpan.FromHours(25));
        return true;
    }
}
