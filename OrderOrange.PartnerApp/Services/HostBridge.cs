using Microsoft.JSInterop;

namespace OrderOrange.PartnerApp.Services;

/// <summary>
/// The browser portal asks ITS OWN host for two map helpers — <c>/placesearch</c> (Google Places,
/// key kept on the server) and <c>/maplink</c> (resolve a shared maps link). Inside the app there
/// is no such host, so index.html routes those two fetches here and .NET forwards them to the
/// partner web host. Native HTTP has no CORS, so nothing changes on the server.
/// </summary>
public static class HostBridge
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record Reply(int Status, string Body);

    [JSInvokable]
    public static async Task<Reply> HostFetch(string pathAndQuery)
    {
        // Only the two helpers, never an arbitrary URL.
        if (!(pathAndQuery.StartsWith("/placesearch?", StringComparison.Ordinal)
              || pathAndQuery.StartsWith("/maplink?", StringComparison.Ordinal)))
            return new Reply(404, "");
        try
        {
            using var response = await Http.GetAsync(AppConfig.PartnerHost.TrimEnd('/') + pathAndQuery);
            return new Reply((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        catch
        {
            return new Reply(502, "");
        }
    }
}
