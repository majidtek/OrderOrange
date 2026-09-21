using OrderOrange.ClientCore.Server;
using OrderOrange.ClientCore;
using OrderOrange.ClientCore.Services;
using OrderOrange.ClientWeb.Components;
using OrderOrange.ClientWeb.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Caching.Memory;

// The customer-facing ordering app — browse, cart, checkout, live tracking.
var builder = WebApplication.CreateBuilder(args);

// Holds the generated sitemap between crawler visits.
builder.Services.AddMemoryCache();

// The visitor's own address is only knowable during the first, ordinary HTTP render;
// once the circuit is live there is no request to read it from. The layout captures it
// there and carries it for the rest of the session.
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    // DetailedErrors sends the real exception text to the browser instead of the
    // generic "An unhandled error has occurred", so a circuit failure can be read
    // off the page rather than guessed at.
    .AddInteractiveServerComponents(o =>
    {
        o.DetailedErrors = true;
        // Phones background the browser constantly; 15 minutes of retention means
        // coming back resumes the session instead of losing the basket-in-progress.
        o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(15);
        o.DisconnectedCircuitMaxRetained = 200;
    });

// Circuit failures otherwise vanish: log them where the stdout file can capture them.
builder.Services.AddScoped<CircuitHandler, LoggingCircuitHandler>();

// Voice notes and photo attachments travel browser->server over the Blazor SignalR
// circuit as data URLs; the 32 KB default cap silently swallowed them.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o => o.MaximumReceiveMessageSize = 4 * 1024 * 1024);

builder.Services.AddClientCoreServer(builder.Configuration["Api:BaseUrl"] ?? "https://127.0.0.1:8520/");

// A browser reload starts a new circuit, which would otherwise mean a new empty
// AppState and an unwanted trip back to the login screen.
builder.Services.AddScoped<ISessionPersistence, BrowserSessionPersistence>();

// The cart lives for the circuit — one tab, one cart.
builder.Services.AddScoped<CartState>();
builder.Services.AddScoped<TableBrandState>();

// The offline AI ordering assistant — per-circuit conversation over the same
// ApiClient/CartState the rest of the app uses. No external AI service.
builder.Services.AddScoped<OrderBot>();

// Same secret in every app so a receipt QR signed here verifies there.
OrderOrange.Shared.BillCode.UseSecret(builder.Configuration["Bill:Secret"]);
OrderOrange.Shared.TableCode.UseSecret(builder.Configuration["Bill:Secret"]);

var app = builder.Build();

// One canonical home: the bare domain hops to www. Google's sign-in button only
// renders on origins the OAuth client knows, and it knows www — a customer landing
// on orderorange.com would otherwise get a login sheet with no Google button.
app.Use(async (context, next) =>
{
    // Only the DEFAULT port hops: the staging copy answers on orderorange.com:9443,
    // and hopping that to www would silently land the request on production.
    if (context.Request.Host.Host.Equals("orderorange.com", StringComparison.OrdinalIgnoreCase)
        && context.Request.Host.Port is null or 443)
    {
        context.Response.Redirect(
            "https://www.orderorange.com" + context.Request.Path + context.Request.QueryString, permanent: true);
        return;
    }
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles(new StaticFileOptions
{
    // the POS slideshow pictures are content-named; let browsers and Cloudflare keep them a year
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/pos"))
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    },
});

// The PAGE itself must never be reused stale: a cached document keeps pointing at
// last week's stylesheet, and every deploy looks broken to whoever held the tab.
// Static assets stay cacheable — their ?v= stamps do the busting.
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

app.UseAntiforgery();
// The landing pages lost the country from their addresses (2026-09-21); the old ones point on.
app.MapGet("/pos-system-oman", (HttpContext ctx) => Results.Redirect("/pos" + ctx.Request.QueryString.Value, permanent: true));
app.MapGet("/pos-system", (HttpContext ctx) => Results.Redirect("/pos" + ctx.Request.QueryString.Value, permanent: true));
app.MapGet("/food-delivery-oman", (HttpContext ctx) => Results.Redirect("/food-delivery" + ctx.Request.QueryString.Value, permanent: true));

// ---- www.orderorange.com/guide: the partner guide at its short address ----
// The pages are generated on the partner site (guide.html, guide.xx.html, guide/*.jpg) and
// stay there as the source; this serves them here, points every canonical and language
// link at the short address, and keeps each page a while so a crawler cannot make us fetch
// it a thousand times. Guide:Source lets staging read its own partner site.
{
    var guideSource = (app.Configuration["Guide:Source"] ?? "https://partner.orderorange.com").TrimEnd('/');
    const string guideHome = "https://www.orderorange.com/guide";
    string[] guideLangs = ["ar", "fa", "ur", "hi", "tr", "de", "fr", "es", "it", "pt", "ru", "ja", "zh"];
    var guideHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    app.MapGet("/guide/{lang?}", async (string? lang, HttpContext ctx, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
    {
        if (lang == "en") return Results.Redirect("/guide" + ctx.Request.QueryString.Value, permanent: true);
        if (lang is not null && !guideLangs.Contains(lang)) return Results.NotFound();
        var key = "guide:" + (lang ?? "en");
        if (!cache.TryGetValue(key, out string? html) || html is null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, guideSource + "/guide" + (lang is null ? "" : "." + lang) + ".html");
            req.Headers.Add("X-Guide-Proxy", "1");
            using var res = await guideHttp.SendAsync(req);
            if (!res.IsSuccessStatusCode) return Results.StatusCode(502);
            html = await res.Content.ReadAsStringAsync();
            html = System.Text.RegularExpressions.Regex.Replace(html, @"https://partner\.orderorange\.com/guide\.([a-z]{2})\.html", guideHome + "/$1");
            html = html.Replace("https://partner.orderorange.com/guide.html", guideHome);
            // pictures are written relative to the page; under /guide/xx that would miss
            html = html.Replace("\"guide/", "\"/guide/").Replace("'guide/", "'/guide/").Replace("(guide/", "(/guide/");
            cache.Set(key, html, TimeSpan.FromMinutes(10));
        }
        ctx.Response.Headers.CacheControl = "public, max-age=600";
        return Results.Content(html, "text/html; charset=utf-8");
    });

    app.MapGet("/guide/{file}.{ext}", async (string file, string ext, HttpContext ctx, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(file, "^[A-Za-z0-9_-]+$") || ext is not ("jpg" or "jpeg" or "png" or "webp" or "svg" or "gif"))
            return Results.NotFound();
        var key = "guide-img:" + file + "." + ext;
        if (!cache.TryGetValue(key, out byte[]? bytes) || bytes is null)
        {
            using var res = await guideHttp.GetAsync(guideSource + "/guide/" + file + "." + ext);
            if (!res.IsSuccessStatusCode) return Results.NotFound();
            bytes = await res.Content.ReadAsByteArrayAsync();
            cache.Set(key, bytes, TimeSpan.FromHours(6));
        }
        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.File(bytes, ext switch { "jpg" or "jpeg" => "image/jpeg", "png" => "image/png", "webp" => "image/webp", "svg" => "image/svg+xml", _ => "image/gif" });
    });
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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

// The picture a shared link shows. WhatsApp, Telegram and the rest fetch og:image with a
// crawler that renders no JS and no SVG, so every store needs its image at a plain raster
// URL. The banner photo leads (link previews are wide), the logo stands in, and a missing
// store falls back to the site's own card. Cached aggressively: one WhatsApp group can
// hit this hundreds of times in a minute.
app.MapGet("/og/{id:int}.jpg", async (int id,
    OrderOrange.ClientCore.Services.ApiClient api,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    var key = $"og:{id}";
    if (!cache.TryGetValue(key, out (byte[] Bytes, string Mime) img))
    {
        string? dataUri = null;
        try
        {
            var photos = await api.GetStorePhotosAsync(id);
            dataUri = photos?.FirstOrDefault()?.Data;
            if (dataUri is null)
            {
                var card = (await api.GetRestaurantsByIdsAsync([id]))?.FirstOrDefault();
                dataUri = card?.LogoPhoto;
            }
        }
        catch { /* fall through to the site card */ }

        if (dataUri is not null && dataUri.StartsWith("data:image/"))
        {
            var comma = dataUri.IndexOf(',');
            var mime = dataUri[5..dataUri.IndexOf(';')];
            img = (Convert.FromBase64String(dataUri[(comma + 1)..]), mime);
        }
        else if (dataUri is not null && dataUri.StartsWith("http"))
        {
            // Since the media-URL release the API hands out LINKS, not inline images —
            // fetch the bytes once and keep serving them from here, so cards and share
            // previews stay on a plain same-origin URL.
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var resp = await http.GetAsync(dataUri);
                var mime = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (!resp.IsSuccessStatusCode || !mime.StartsWith("image/"))
                    return Results.Redirect("/og-default.jpg");
                img = (await resp.Content.ReadAsByteArrayAsync(), mime);
            }
            catch { return Results.Redirect("/og-default.jpg"); }
        }
        else
        {
            return Results.Redirect("/og-default.jpg");
        }
        cache.Set(key, img, TimeSpan.FromHours(1));
    }
    return Results.File(img.Bytes, img.Mime);
});

// The sitemap, built from the database rather than kept by hand, so a shop onboarded
// today is offered to Google today. Only real shops are listed — see the API's
// /restaurants/sitemap for why the generated catalog is deliberately left out.
//
// Cached for an hour: a crawler may ask often, and this must never become a way to
// hammer the API from outside.
app.MapGet("/sitemap.xml", async (
    OrderOrange.ClientCore.Services.ApiClient api,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    const string key = "sitemap.xml";
    if (!cache.TryGetValue(key, out string? xml) || xml is null)
    {
        var stores = await api.GetSitemapStoresAsync() ?? [];
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">""");
        // Every page exists once per language (?lang=xx, English bare); the alternates
        // tell Google they are translations of one page, not fourteen duplicates.
        string[] langs = ["en", "ar", "fa", "ur", "hi", "de", "es", "fr", "it", "ja", "pt", "ru", "tr", "zh"];
        string Alternates(string loc) => string.Concat(langs.Select(l => $"<xhtml:link rel=\"alternate\" hreflang=\"{l}\" href=\"{(l == "en" ? loc : loc + "?lang=" + l)}\"/>"))
                                         + $"<xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{loc}\"/>";
        sb.AppendLine($"  <url><loc>https://www.orderorange.com/</loc><lastmod>{today}</lastmod><changefreq>daily</changefreq><priority>1.0</priority>{Alternates("https://www.orderorange.com/")}</url>");
        // The search landing pages: the hub, one page per cuisine, one per area — the same
        // slugs the pages and the crawler footer use (SeoSlugs), so nothing can drift.
        sb.AppendLine($"  <url><loc>https://www.orderorange.com{OrderOrange.ClientWeb.Seo.SeoSlugs.Hub}</loc><lastmod>{today}</lastmod><changefreq>daily</changefreq><priority>0.9</priority>{Alternates("https://www.orderorange.com" + OrderOrange.ClientWeb.Seo.SeoSlugs.Hub)}</url>");
        // The till page: one address, no store list behind it, changed only when we say so.
        sb.AppendLine($"  <url><loc>https://www.orderorange.com{OrderOrange.ClientWeb.Seo.SeoSlugs.Pos}</loc><lastmod>{today}</lastmod><changefreq>weekly</changefreq><priority>0.9</priority>{Alternates("https://www.orderorange.com" + OrderOrange.ClientWeb.Seo.SeoSlugs.Pos)}</url>");
        // The partner guide, now at its short address, one page per language under /guide/xx.
        sb.AppendLine($"  <url><loc>https://www.orderorange.com/guide</loc><lastmod>{today}</lastmod><changefreq>weekly</changefreq><priority>0.8</priority>"
            + string.Concat(langs.Select(l => $"<xhtml:link rel=\"alternate\" hreflang=\"{l}\" href=\"https://www.orderorange.com/guide{(l == "en" ? "" : "/" + l)}\"/>"))
            + "<xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"https://www.orderorange.com/guide\"/></url>");
        // Every cuisine a listed shop carries, main and extra alike — the cards know both.
        List<OrderOrange.Shared.RestaurantCardDto> cards = [];
        try { cards = (await api.BrowsePageAsync(null, take: 60) ?? []).Where(x => x.Id >= 5_000_200).ToList(); } catch { }
        var cardCuisines = cards.SelectMany(x => x.Cuisines is { Count: > 0 } ? x.Cuisines : [x.Cuisine]);
        foreach (var c in stores.Select(x => x.Cuisine).Concat(cardCuisines).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c))
        {
            var loc = "https://www.orderorange.com" + OrderOrange.ClientWeb.Seo.SeoSlugs.CuisinePath(c);
            sb.AppendLine($"  <url><loc>{loc}</loc><changefreq>weekly</changefreq><priority>0.7</priority>{Alternates(loc)}</url>");
        }
        foreach (var a in OrderOrange.ClientWeb.Seo.SeoSlugs.Areas(stores.Select(x => x.Area)))
        {
            var loc = "https://www.orderorange.com" + OrderOrange.ClientWeb.Seo.SeoSlugs.AreaPath(a);
            sb.AppendLine($"  <url><loc>{loc}</loc><changefreq>weekly</changefreq><priority>0.7</priority>{Alternates(loc)}</url>");
        }
        foreach (var s in stores)
        {
            // The slug IS the shop's canonical address; only slug-less shops fall back
            // to the numeric form. Listing both would offer Google duplicates.
            var loc = s.Slug.Length > 0
                ? $"https://www.orderorange.com/{s.Slug}"
                : $"https://www.orderorange.com/restaurant/{s.Id}";
            var name = System.Security.SecurityElement.Escape(s.Name) ?? "";
            sb.AppendLine($"  <url><loc>{loc}</loc><changefreq>weekly</changefreq><priority>0.8</priority>{Alternates(loc)}"
                          + $"<image:image><image:loc>https://www.orderorange.com/og/{s.Id}.jpg</image:loc><image:title>{name}</image:title></image:image></url>");
        }
        sb.AppendLine("</urlset>");

        xml = sb.ToString();
        cache.Set(key, xml, TimeSpan.FromHours(1));
    }
    return Results.Content(xml, "application/xml");
});

app.Run();
