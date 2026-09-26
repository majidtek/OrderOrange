using System.Text;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default")));
        


builder.Services.AddScoped<TokenService>();

// Stateless and thread-safe; Google's key set is cached inside the library, so sharing
// one instance means one key fetch for the process rather than one per sign-in.
builder.Services.AddSingleton<GoogleTokenVerifier>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<PushSender>();   // Web Push to partner devices
builder.Services.AddSingleton<LoginCodeStore>();
builder.Services.AddSingleton<RegistrationThrottle>();
builder.Services.AddMemoryCache();

// Order chat lives in MongoDB, not SQL. One client for the process — the driver
// pools connections internally and is designed to be shared.
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>()
      .GetDatabase(builder.Configuration["Mongo:Database"] ?? "wajibat"));
builder.Services.AddSingleton<ChatStore>();
builder.Services.AddSingleton<CatalogStore>();

// Page views live in MongoDB: append-only, unbounded, and nothing joins to them.
builder.Services.AddSingleton<VisitStore>();
builder.Services.AddSingleton<SurveyStore>();
builder.Services.AddSingleton<SupportStore>();
builder.Services.AddSingleton<PromotionStore>();
builder.Services.AddScoped<OrderOrange.ApiServer.Services.PromotionEngine>();   // reads the order book, so it rides the request's DbContext
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.TableChatStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.CommunityChatStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.AdminAlertStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.BotChatStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.KdsStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.StorePrefsStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.WasteStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.WarehouseStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.LoyaltyStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.PresenceStore>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.AdminAlerts>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.ContactStore>();
// A shop posting to its own Instagram: the connection per store, and the Graph calls.
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.InstagramStore>();
builder.Services.AddHttpClient("ig", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<OrderOrange.ApiServer.Services.InstagramGraph>();
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.InstagramQueueStore>();
// Planned posts go out on their own minute, with each store's own token.
builder.Services.AddSingleton<OrderOrange.ApiServer.Services.InstagramGraph>();
builder.Services.AddHostedService<OrderOrange.ApiServer.Services.InstagramScheduler>();

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();

// JSON travels compressed: a 130 KB menu becomes ~25 KB on the wire, and the
// WebAssembly partner app fetches everything over the public internet now.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

// Same secret in every app so a receipt QR signed here verifies there.
OrderOrange.Shared.BillCode.UseSecret(builder.Configuration["Bill:Secret"]);
OrderOrange.Shared.TableCode.UseSecret(builder.Configuration["Bill:Secret"]);
OrderOrange.Shared.ContractCode.UseSecret(builder.Configuration["Bill:Secret"]);

var app = builder.Build();

// "dotnet run -- --test-email you@example.com" — proves the SMTP settings work before a
// customer is the one who discovers they don't. Sends a real message and says exactly
// what failed, which a silent false from deep inside a sign-in request never would.
if (args.Contains("--test-email"))
{
    var to = args.SkipWhile(a => a != "--test-email").Skip(1).FirstOrDefault();
    if (string.IsNullOrWhiteSpace(to))
    {
        app.Logger.LogError("Usage: --test-email someone@example.com");
        return;
    }

    var sender = app.Services.GetRequiredService<EmailSender>();
    if (!sender.IsConfigured)
    {
        app.Logger.LogError("Smtp:Host is empty — nothing to test. Fill in the Smtp section of appsettings.json.");
        return;
    }

    app.Logger.LogInformation("Sending a test message to {To}…", to);
    var sent = await sender.SendAsync(to, "OrderOrange test",
        "If you are reading this, sign-in codes will reach your customers.");
    app.Logger.LogInformation(sent
        ? "SENT. Check the inbox (and the spam folder)."
        : "FAILED — the error above says why. Common causes: using the account password instead of an app password, or 2-Step Verification not enabled.");
    return;
}

// "dotnet run -- --seed-demo-accounts" — creates or resets one login per app.
if (args.Contains("--seed-demo-accounts"))
{
    using var demoScope = app.Services.CreateScope();
    var accounts = await DemoAccounts.SeedAsync(
        demoScope.ServiceProvider.GetRequiredService<AppDbContext>(), app.Logger);
    foreach (var a in accounts)
        app.Logger.LogInformation("  {App,-16} {Email,-30} {Password,-10} {Note}",
            a.App, a.Email, a.Password, a.Note ?? "");
    return;
}

// One-off catalog copy: "dotnet run -- --migrate-catalog". Kept out of normal startup
// because five million restaurants is not something to re-scan on every boot.
if (args.Contains("--migrate-catalog"))
{
    var mongo = app.Services.GetRequiredService<IMongoDatabase>();
    await CatalogIndexes.EnsureAsync(mongo);
    var migrator = new CatalogMigrator(
        builder.Configuration.GetConnectionString("Default")!, mongo, app.Logger);

    var started = DateTime.Now;
    var report = await migrator.RunAsync();
    foreach (var (collection, count) in report)
        app.Logger.LogInformation("  {Collection,-20} {Count,12:N0}", collection, count);
    app.Logger.LogInformation("Catalog migration finished in {Elapsed}.", DateTime.Now - started);
    return;
}

// Seed the database (creates it if missing). Best-effort so the API still starts if SQL is offline.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scale = builder.Configuration["Seed:Scale"];

        if (string.Equals(scale, "Production", StringComparison.OrdinalIgnoreCase))
        {
            // A real shop. Create the schema and stop: no invented restaurants, no
            // invented customers, no invented order history. Reference data (the store
            // types) and the staff accounts are put in by the cutover script, not here —
            // seeding them on every start would silently resurrect anything an
            // administrator deliberately deleted.
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            // "Big" (default) generates ~90 days of data for a lively demo; tests pin "Demo"
            // so their assertions stay deterministic.
            var big = !string.Equals(scale, "Demo", StringComparison.OrdinalIgnoreCase);
            await DbSeeder.SeedAsync(db, big);
        }

        // EnsureCreated never alters an existing table, so columns added after launch
        // are patched in here — idempotent, a no-op on every start once applied.
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('dbo.StoreTabLines','AddedAt') IS NULL
                ALTER TABLE dbo.StoreTabLines ADD AddedAt datetime2 NOT NULL CONSTRAINT DF_StoreTabLines_AddedAt DEFAULT '2000-01-01';
            IF COL_LENGTH('dbo.StoreTabLines','Source') IS NULL
                ALTER TABLE dbo.StoreTabLines ADD Source nvarchar(8) NULL;
            IF COL_LENGTH('dbo.Restaurants','KitchenLanguage') IS NULL
                ALTER TABLE dbo.Restaurants ADD KitchenLanguage nvarchar(8) NOT NULL CONSTRAINT DF_Restaurants_KitchenLanguage DEFAULT '';
            """);

        // Products carry multilingual keywords in a DB column; fill any that are blank
        // so search-by-keyword ("آب" or "مسافي" → Water) works for every stored item.
        var blank = await db.MenuItems.Where(i => i.SearchKeywords == "")
            .Include(i => i.Category).ToListAsync();
        foreach (var item in blank)
            item.SearchKeywords = SearchAliases.KeywordsFor(item.Name, item.Category?.Name);
        if (blank.Count > 0) await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database seed failed — is SQL Server reachable?");
    }

    // The catalog lives in MongoDB. Production is filled once by --migrate-catalog;
    // this only does anything when the collection is empty (tests, or a fresh box).
    try
    {
        var mongo = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        await CatalogIndexes.EnsureAsync(mongo);
        await CatalogBootstrap.SyncIfEmptyAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), mongo, app.Logger);
        var catalogStore = scope.ServiceProvider.GetRequiredService<CatalogStore>();
        await catalogStore.EnsureSequencesAsync();
        // products no longer wait for review (2026-09-26): let through whatever was still pending
        var released = await catalogStore.ApproveAllPendingAsync();
        if (released > 0) app.Logger.LogInformation("Catalog: {Count} pending products approved (review gate removed)", released);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Mongo catalog bootstrap failed — is mongod running?");
    }

    // Every store carries the house furniture: the Drinks/Water shelf and the
    // two-salon, sixteen-table floor. Idempotent per store, and in its OWN try
    // so a bootstrap hiccup can't block it.
    //
    // This is a BACKFILL for stores born before the furniture existed — creation
    // paths seed their own. With the five-million-store demo world in the database
    // a per-store sweep would never finish booting, so it only runs on small
    // databases (dev boxes and the test suite); the demo stores keep no furniture
    // until a real owner takes one over.
    try
    {
        var catalogStore = scope.ServiceProvider.GetRequiredService<CatalogStore>();
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await seedDb.Restaurants.CountAsync() <= 1000)
        {
            var storeIds = await seedDb.Restaurants.Select(r => r.Id).ToListAsync();
            foreach (var storeId in storeIds)
            {
                await catalogStore.SeedDefaultMenuAsync(storeId);
                await DefaultFloor.SeedAsync(seedDb, storeId);
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Default store furniture seeding failed.");
    }

    // Hand-created stores must be in the inverted word index or search can't see
    // them past the millions of catalog rows. New ones are indexed at creation;
    // this sweep catches any that arrived another way (the production merge did).
    // Newest 500 only — an id-ordered seek, never a scan.
    try
    {
        var wordDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (wordDb.Database.IsRelational())
        {
            // Hand-created stores live at the TOP of the id range (the bulk catalog was
            // generated below them), so the newest few hundred is where a missing index
            // can hide. Re-indexed unconditionally: cheap for this many, and it also
            // repairs a store whose name changed while the index write failed.
            var newest = await wordDb.Restaurants.OrderByDescending(r => r.Id).Take(60).ToListAsync();
            foreach (var store in newest)
            {
                var has = await wordDb.RestaurantWords.AnyAsync(w => w.RestaurantId == store.Id);
                if (!has) await StoreWordIndex.ReindexAsync(wordDb, store);
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Store word-index sweep failed.");
    }

    // Chat moved from SQL to MongoDB. Carry the old conversations across once, keeping
    // their original ids so clients polling "after id N" don't re-read or skip messages.
    try
    {
        var chat = scope.ServiceProvider.GetRequiredService<ChatStore>();
        // Survey indexes: by store for the list, by survey for the answer stream.
        try { await scope.ServiceProvider.GetRequiredService<SurveyStore>().EnsureIndexesAsync(); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Survey indexes could not be ensured"); }
        try { await scope.ServiceProvider.GetRequiredService<SupportStore>().EnsureIndexesAsync(); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Support ticket indexes could not be ensured"); }
        try { await scope.ServiceProvider.GetRequiredService<PromotionStore>().EnsureIndexesAsync(); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Promotion indexes could not be ensured"); }
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Raw SQL only works against a relational provider — tests run on EF InMemory
        // and simply start with an empty Mongo chat collection.
        //
        // The table only exists in databases that predate the move to Mongo. A database
        // created fresh has never had one, so ask before selecting from it rather than
        // logging an alarming failure on every single start of a healthy server.
        var legacyTableExists = db.Database.IsRelational() && await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'ChatMessages'")
            .FirstOrDefaultAsync() > 0;

        if (legacyTableExists && await chat.TotalAsync() == 0)
        {
            var legacy = await db.Database
                .SqlQuery<ChatDoc>($@"
                    SELECT Id, OrderId, SenderUserId, SenderName, SenderRole,
                           Text, AttachmentData, AttachmentType, FileName, At
                    FROM ChatMessages")
                .ToListAsync();

            if (legacy.Count > 0)
            {
                await chat.ImportAsync(legacy);
                await chat.EnsureSequenceAtLeastAsync(legacy.Max(m => m.Id));
                app.Logger.LogInformation("Imported {Count} chat messages from SQL into MongoDB.", legacy.Count);
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Chat import into MongoDB failed — is mongod running?");
    }
}

// Keep the heavy admin dashboard aggregates warm so login lands instantly.
AdminDashboardService.StartWarmer(app.Services, app.Logger);

app.UseResponseCompression();
app.UseCors();
app.UseAuthentication();

// Stamp "last seen" on any authenticated call, throttled to once a minute per user
// so the drivers' 5-second polling doesn't hammer the Users table. The same beat also
// notes WHERE it came from (address + app) in Mongo, because a signed-in tab left open
// keeps beating without anyone visiting a page — the admin board must tell the two apart.
var lastBeat = new System.Collections.Concurrent.ConcurrentDictionary<int, DateTime>();
app.Use(async (context, next) =>
{
    await next();
    var idClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (idClaim is not null && int.TryParse(idClaim, out var userId))
    {
        var now = DateTime.Now;
        if (lastBeat.TryGetValue(userId, out var prev) && (now - prev).TotalSeconds < 60) return;
        lastBeat[userId] = now;
        try
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE Users SET LastSeenAt = GETDATE() WHERE Id = {0} AND (LastSeenAt IS NULL OR DATEDIFF(second, LastSeenAt, GETDATE()) > 60)",
                userId);
            await context.RequestServices.GetRequiredService<OrderOrange.ApiServer.Services.PresenceStore>()
                .BeatAsync(userId, BeatIp(context), BeatApp(context));
        }
        catch { /* presence is best-effort — never fail a request over it */ }
    }
});

// The caller's address for the heartbeat: Cloudflare's header, then the first
// X-Forwarded-For entry, then the socket — never a loopback address (that is our own proxy).
static string BeatIp(HttpContext ctx)
{
    var socket = ctx.Connection.RemoteIpAddress;
    if (socket is null || System.Net.IPAddress.IsLoopback(socket))
    {
        foreach (var header in new[] { "CF-Connecting-IP", "X-Client-IP", "X-Forwarded-For" })
        {
            var v = ctx.Request.Headers[header].ToString();
            if (string.IsNullOrWhiteSpace(v)) continue;
            var first = v.Split(',')[0].Trim();
            if (System.Net.IPAddress.TryParse(first, out var p) && !System.Net.IPAddress.IsLoopback(p)) return first;
        }
        return "";
    }
    return socket.ToString();
}

// Which app is beating, read off the page that made the call.
static string BeatApp(HttpContext ctx)
{
    var from = ctx.Request.Headers.Origin.ToString();
    if (string.IsNullOrEmpty(from)) from = ctx.Request.Headers.Referer.ToString();
    from = from.ToLowerInvariant();
    if (from.Contains("partner.") || from.Contains(":9444")) return "Partner";
    if (from.Contains("admin.") || from.Contains(":9445")) return "Admin";
    if (from.Contains("delivery.") || from.Contains("rider.")) return "Delivery";
    if (from.Contains("orderorange.com")) return "Customer";
    return "API";
}

app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
