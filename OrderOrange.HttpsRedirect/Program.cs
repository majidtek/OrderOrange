// Port 80 for every OrderOrange hostname ends here, and the only thing this app ever
// says is "use https". The real sites hold https bindings only; nothing is served in
// the clear. The apex is sent straight to www so the visitor makes one hop, not two.
var app = WebApplication.CreateBuilder(args).Build();

app.Run(ctx =>
{
    var host = ctx.Request.Host.Host;
    if (host.Equals("orderorange.com", StringComparison.OrdinalIgnoreCase)) host = "www.orderorange.com";
    ctx.Response.Redirect($"https://{host}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}", permanent: true);
    return Task.CompletedTask;
});

app.Run();
