using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OrderOrange.ClientCore;
using OrderOrange.ClientCore.Services;
using OrderOrange.AdminWeb.Components;

// The platform back office — KPIs, approvals, users, orders, coupons — in the browser.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

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
        builder.Configuration.AddInMemoryCollection(flat);
    }
}
catch { /* falls back to defaults below */ }

var apiBase = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(apiBase)) apiBase = "https://api.orderorange.com/";
builder.Services.AddClientCore(apiBase);
builder.Services.AddScoped<ISessionPersistence, BrowserSessionPersistence>();

var translations = new HttpTranslationLoader(self, "i18n/");
builder.Services.AddSingleton<ITranslationLoader>(translations);

var host = builder.Build();
await translations.LoadIndexAsync();
await translations.EnsureAsync(TranslationCatalog.DefaultLocale);
await host.Services.GetRequiredService<LanguageService>().EnsureLoadedAsync();
await host.RunAsync();
