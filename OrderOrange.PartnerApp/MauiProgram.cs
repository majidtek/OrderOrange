using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using OrderOrange.ClientCore;
using OrderOrange.ClientCore.Services;
using OrderOrange.PartnerApp.Services;

namespace OrderOrange.PartnerApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // The shared portal pages ask this to show "Printers" instead of "download the app".
        HostInfo.IsNativeApp = true;
        HostInfo.SiteBase = AppConfig.PartnerHost;

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        // What the browser portal reads from its host's /config.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = AppConfig.ApiBaseUrl,
            ["ClientUrl"] = AppConfig.ClientUrl,
        });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

        // Exactly the browser portal's registrations (OrderOrange.RestaurantWeb/Program.cs) …
        builder.Services.AddClientCore(AppConfig.ApiBaseUrl);
        // … the session lives in the WebView's own storage: "keep me signed in" survives a restart.
        builder.Services.AddScoped<ISessionPersistence, BrowserSessionPersistence>();
        builder.Services.AddScoped<OrderOrange.RestaurantWeb.Services.ReceiptPrinter>();

        // … except the language files, which come from the app package instead of over HTTP.
        builder.Services.AddSingleton<PackageTranslationLoader>();
        builder.Services.AddSingleton<ITranslationLoader>(sp => sp.GetRequiredService<PackageTranslationLoader>());

        return builder.Build();
    }
}
