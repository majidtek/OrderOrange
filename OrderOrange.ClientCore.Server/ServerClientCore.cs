using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OrderOrange.ClientCore.Services;

namespace OrderOrange.ClientCore.Server;

/// <summary>
/// The language choice in data-protected local storage — what the Blazor Server apps
/// have always used, so nobody's saved language is lost by this split.
/// </summary>
public sealed class ProtectedLocaleStore(ProtectedLocalStorage storage) : ILocaleStore
{
    private const string Key = "mf.lang";

    public async Task<string?> GetAsync()
    {
        try
        {
            var saved = await storage.GetAsync<string>(Key);
            return saved.Success ? saved.Value : null;
        }
        catch { return null; }   // storage not ready during prerender
    }

    public async Task SetAsync(string locale)
    {
        try { await storage.SetAsync(Key, locale); }
        catch { /* best-effort persistence */ }
    }
}

public static class ServerClientCoreExtensions
{
    /// <summary>
    /// ClientCore plus the two things only a server host can do: forward the visitor's real
    /// address (every API call otherwise arrives from 127.0.0.1) and keep the chosen
    /// language in data-protected storage.
    /// </summary>
    public static IServiceCollection AddClientCoreServer(this IServiceCollection services, string apiBaseUrl)
    {
        services.AddHttpContextAccessor();
        services.AddClientCore(apiBaseUrl, sp =>
            sp.GetService<IHttpContextAccessor>()?.HttpContext?.Connection.RemoteIpAddress?.ToString());
        // Registered after AddClientCore so this one wins the resolution.
        services.AddScoped<ILocaleStore, ProtectedLocaleStore>();
        return services;
    }
}
