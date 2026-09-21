// Static host for the WebAssembly admin panel: serves the files, its own /config, and
// sends unknown paths to index.html for deep links.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        if ((ctx.Response.ContentType ?? "").Contains("text/html", StringComparison.OrdinalIgnoreCase))
            ctx.Response.Headers.CacheControl = "no-cache";
        return Task.CompletedTask;
    });
    await next();
});

app.UseBlazorFrameworkFiles();
app.UseRouting();
app.MapStaticAssets();

app.MapGet("/appsettings.json", (IConfiguration config) => Results.Json(new
{
    Api = new { BaseUrl = config["Api:BaseUrl"] ?? "https://api.orderorange.com/" },
    ClientUrl = config["ClientUrl"] ?? "https://orderorange.com",
    PartnerUrl = config["PartnerUrl"] ?? "https://partner.orderorange.com",
}));
app.MapGet("/config", (IConfiguration config) => Results.Json(new
{
    Api = new { BaseUrl = config["Api:BaseUrl"] ?? "https://api.orderorange.com/" },
    ClientUrl = config["ClientUrl"] ?? "https://orderorange.com",
    PartnerUrl = config["PartnerUrl"] ?? "https://partner.orderorange.com",
}));

app.MapFallbackToFile("index.html");
app.Run();
