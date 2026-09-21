using Microsoft.Extensions.FileProviders;

// api.orderorange.com — the API's public front door.
//
// The API itself is a self-hosted Kestrel exe (it needs the Administrator's LocalDB
// instance, which an IIS pool identity cannot open), so it cannot sit on IIS's 443
// directly. This tiny IIS-hosted app owns the api.orderorange.com binding and hands
// every request straight to the API's loopback endpoint. Nothing here knows about
// orders or menus; it only knows where the API lives.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Let's Encrypt proves ownership by fetching /.well-known/acme-challenge/<token>
// over plain http. win-acme writes the token into the site folder; serve it from
// disk, never forward it to the API.
var wellKnown = Path.Combine(app.Environment.ContentRootPath, ".well-known");
Directory.CreateDirectory(wellKnown);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(wellKnown),
    RequestPath = "/.well-known",
    ServeUnknownFileTypes = true,
    DefaultContentType = "text/plain",
});

app.MapReverseProxy();
app.Run();
