using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using OrderOrange.ClientCore.Services;

namespace OrderOrange.ClientCore;

/// <summary>
/// Everything the four OrderOrange apps share, registered in one call. Browser-neutral:
/// the same registration serves a Blazor Server circuit and a WebAssembly page. A server
/// host adds its extras through OrderOrange.ClientCore.Server (real visitor address,
/// data-protected locale storage).
/// </summary>
public static class ClientCoreExtensions
{
    /// <param name="apiBaseUrl">Where the API lives — loopback for a server app, the public origin for a browser app.</param>
    /// <param name="clientIp">
    /// Server apps only: the visitor's address, forwarded as X-Client-IP because every call
    /// would otherwise reach the API from 127.0.0.1. A browser app leaves it null — the
    /// API then sees the visitor directly.
    /// </param>
    public static IServiceCollection AddClientCore(this IServiceCollection services, string apiBaseUrl,
        Func<IServiceProvider, string?>? clientIp = null)
    {
        services.AddMudServices();

        services.AddScoped<AppState>();
        services.AddScoped<ILocaleStore, JsLocaleStore>();
        services.AddScoped(sp => new LanguageService(
            sp.GetRequiredService<ILocaleStore>(),
            sp.GetRequiredService<IJSRuntime>(),
            sp.GetService<ITranslationLoader>()));

        services.AddScoped(sp =>
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(apiBaseUrl),
                Timeout = TimeSpan.FromSeconds(20)
            };
            var ip = clientIp?.Invoke(sp);
            if (!string.IsNullOrEmpty(ip))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Client-IP", ip);
            return client;
        });
        services.AddScoped<ApiClient>();

        // Browser hosts replace this so a reload doesn't sign the user out.
        services.AddScoped<ISessionPersistence, NoSessionPersistence>();
        return services;
    }
}
