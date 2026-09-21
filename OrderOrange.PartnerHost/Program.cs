// Static host for the WebAssembly partner portal. The portal itself runs in the browser
// and talks to the API directly; this process only hands out the files, answers the two
// same-origin map helpers, and sends every unknown path to index.html so deep links work.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpForwarder();
var app = builder.Build();

// Same-origin API: the browser calls THIS site's /api/* and the host forwards it to the
// real API (Api:Upstream — the loopback listener). No second host name, certificate or
// CORS for the browser to trip on; the API still sees the visitor via X-Forwarded-For.
var upstream = app.Configuration["Api:Upstream"];
if (!string.IsNullOrWhiteSpace(upstream))
{
    var forwarderClient = new HttpMessageInvoker(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        UseCookies = false,
        // The loopback API carries a self-signed certificate — that hop is on this box.
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, _, _, _) => new Uri(upstream).IsLoopback,
        },
    });
    app.MapForwarder("/api/{**rest}", upstream.TrimEnd('/'),
        new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(5) },
        Yarp.ReverseProxy.Forwarder.HttpTransformer.Default, forwarderClient);
}

if (!app.Environment.IsDevelopment())
    app.UseHsts();

// The PAGE itself must never be reused stale: a cached document keeps pointing at
// last week's stylesheet, and every deploy looks broken to whoever held the tab.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        if ((ctx.Response.ContentType ?? "").Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            // Only the guide pages are for search engines; the app shell (every other HTML
            // answer, including deep links that fall back to index.html) is not.
            if (!ctx.Request.Path.StartsWithSegments("/guide") && !ctx.Request.Path.Value!.StartsWith("/guide.", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ctx.Request.Path.Value, "/api.html", StringComparison.OrdinalIgnoreCase))
                ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        }
        // The partner guide's screenshots live under /guide/ with a content hash in the
        // file name, so they can be cached for a year by the browser and by Cloudflare;
        // a changed screenshot gets a new name and the page points at it.
        else if (ctx.Request.Path.StartsWithSegments("/guide"))
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        // The runtime loader (dotnet.js carries the boot manifest since .NET 9) names every
        // other file by content hash. Browsers and Cloudflare were keeping it for four hours,
        // so a deploy reached phones only hours later; it must be re-checked on every start,
        // while the hashed files it points at may live in caches for a year.
        else if (ctx.Request.Path.StartsWithSegments("/_framework"))
        {
            // the compression middleware has already rewritten the path to the .br/.gz twin
            var file = System.IO.Path.GetFileName(ctx.Request.Path.Value ?? "");
            if (file.EndsWith(".br") || file.EndsWith(".gz")) file = file[..file.LastIndexOf('.')];
            ctx.Response.Headers.CacheControl =
                file is "dotnet.js" or "blazor.webassembly.js" or "blazor.boot.json"
                    ? "private, no-cache"
                    : "public, max-age=31536000, immutable";
        }
        return Task.CompletedTask;
    });
    await next();
});

// Order matters: the runtime files (_framework/*) are served BEFORE routing, so the
// static-asset endpoints below never intercept them; everything else under wwwroot
// (stylesheets, scripts, i18n) goes through MapStaticAssets, which hands out the
// pre-compressed .br/.gz copies — the 1 MB stylesheet travels as ~160 KB.
// The guide's home is now www.orderorange.com/guide (Guide:ShortBase); the files stay here
// as its source — the customer site fetches them with X-Guide-Proxy — and every old address
// (guide.html, guide.xx.html, /guide) just points on.
{
    var guideShort = (app.Configuration["Guide:ShortBase"] ?? "https://www.orderorange.com/guide").TrimEnd('/');
    app.Use(async (ctx, next) =>
    {
        var m = System.Text.RegularExpressions.Regex.Match(ctx.Request.Path.Value ?? "", @"^/guide(?:\.([a-z]{2}))?(?:\.html)?(?:\.br|\.gz)?/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && !ctx.Request.Headers.ContainsKey("X-Guide-Proxy"))
        {
            var l = m.Groups[1].Value.ToLowerInvariant();
            ctx.Response.Redirect(guideShort + (l is "" or "en" ? "" : "/" + l) + ctx.Request.QueryString.Value, permanent: true);
            return;
        }
        await next();
    });
}
app.UseBlazorFrameworkFiles();
app.UseRouting();
app.MapStaticAssets();

// The browser app's settings come from THIS site's appsettings.json (Api:BaseUrl =
// the public API origin, ClientUrl = the customer site), so a deploy never overwrites
// them and staging/production differ only in the file that already differs.
// Never cacheable: a browser that keeps yesterday's copy would keep talking to
// yesterday's API — the one thing this file must not do.
IResult Settings(HttpContext ctx, IConfiguration config)
{
    ctx.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
    ctx.Response.Headers.Pragma = "no-cache";
    return Results.Json(new
    {
        Api = new { BaseUrl = config["Api:BaseUrl"] ?? "https://api.orderorange.com/" },
        ClientUrl = config["ClientUrl"] ?? "https://orderorange.com",
    });
}
app.MapGet("/appsettings.json", Settings);
app.MapGet("/config", Settings);   // the app asks for this one with a cache-busting stamp

// The map dialog's link box: short goo.gl links get resolved server-side,
// same-origin, so the browser needs no CORS and no direct API access.
app.MapGet("/placesearch", async (string q, string? lang, string? ft, IConfiguration config) =>
{
    if (!OrderOrange.Shared.FormToken.Validate(ft)) return Results.Unauthorized();
    var places = await OrderOrange.ClientCore.Services.MapLinkResolver.SearchPlacesAsync(
        q ?? "", lang, config["GoogleMaps:ApiKey"]);
    return Results.Json(places.Select(p => new { name = p.Name, address = p.Address, lat = p.Lat, lng = p.Lng }));
});

app.MapGet("/maplink", async (string url, string? ft) =>
{
    if (!OrderOrange.Shared.FormToken.Validate(ft)) return Results.Unauthorized();
    var point = await OrderOrange.ClientCore.Services.MapLinkResolver.ResolveAsync(url ?? "");
    return point is null
        ? Results.NotFound()
        : Results.Json(new { lat = point.Value.Lat, lng = point.Value.Lng });
});

// The tablet page for old browsers is plain files under /tablet/. A request for the
// folder itself must land on ITS index, not fall through to the Blazor app's.
app.MapGet("/tablet", (HttpContext ctx) => Results.Redirect("/tablet/index.html" + ctx.Request.QueryString.Value));

app.MapFallbackToFile("index.html");

app.Run();
