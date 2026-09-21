using OrderOrange.ClientCore.Server;
using OrderOrange.ClientCore;
using OrderOrange.ClientCore.Services;
using OrderOrange.DeliveryWeb.Components;

// The driver app — go online, claim deliveries, drive the last mile.
var builder = WebApplication.CreateBuilder(args);

// The visitor own address is only knowable during the first, ordinary HTTP render;
// after the circuit opens there is no request to read it from, and the API would see
// only this server.
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o =>
    {
        // Phones background the browser constantly; the 3-minute default meant coming
        // back to "Reconnecting…" and a reload, with any half-typed form lost. Keep
        // disconnected sessions for 15 minutes so returning simply resumes.
        o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(15);
        o.DisconnectedCircuitMaxRetained = 200;
    });

// Voice notes and photo attachments travel browser->server over the Blazor SignalR
// circuit as data URLs; the 32 KB default cap silently swallowed them.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o => o.MaximumReceiveMessageSize = 4 * 1024 * 1024);

builder.Services.AddClientCoreServer(builder.Configuration["Api:BaseUrl"] ?? "https://127.0.0.1:8520/");
builder.Services.AddScoped<ISessionPersistence, BrowserSessionPersistence>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
