using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OrderOrange.ClientCore;
using OrderOrange.ClientCore.Services;
using OrderOrange.RestaurantWeb.Components;

// The restaurant partner portal — live order board, menu management, settings — running
// entirely in the browser. The API is the only server it talks to; the host that serves
// these files (OrderOrange.PartnerHost) is a static file server plus two map helpers.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Settings come from the host's /config, fetched fresh every start with a cache-busting
// stamp — a stale appsettings.json once sent a staging tester to the production API.
// Api:BaseUrl is the PUBLIC API origin the browser can reach; empty = this site itself
// (the host forwards /api/* to the API, so no second host name, certificate or CORS).
var self = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
try
{
    var live = await self.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>($"config?t={DateTime.UtcNow.Ticks}");
    if (live is not null)
    {
        var flat = new Dictionary<string, string?>();
        foreach (var (key, value) in live)
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.Object)
                foreach (var inner in value.EnumerateObject()) flat[$"{key}:{inner.Name}"] = inner.Value.ToString();
            else flat[key] = value.ToString();
        }
        builder.Configuration.AddInMemoryCollection(flat);   // wins over any cached appsettings.json
    }
}
catch { /* no /config (a plain static host) — appsettings.json stays the source */ }
var apiBase = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(apiBase)) apiBase = builder.HostEnvironment.BaseAddress;
builder.Services.AddClientCore(apiBase);
builder.Services.AddScoped<ISessionPersistence, BrowserSessionPersistence>();

// One shared door to the receipt printer — see ReceiptPrinter.
builder.Services.AddScoped<OrderOrange.RestaurantWeb.Services.ReceiptPrinter>();

// Translations come from this site's own i18n folder, one language at a time.
var translations = new HttpTranslationLoader(self, "i18n/");
builder.Services.AddSingleton<ITranslationLoader>(translations);

var host = builder.Build();

// Before the first paint: the language list, English (the fallback), and whatever
// language this browser chose last time — so the UI never flashes untranslated keys.
await translations.LoadIndexAsync();
await translations.EnsureAsync(TranslationCatalog.DefaultLocale);
await host.Services.GetRequiredService<LanguageService>().EnsureLoadedAsync();

await host.RunAsync();
