using OrderOrange.ApiServer.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace OrderOrange.Tests;

/// <summary>
/// Boots the real API in-memory with the EF InMemory provider instead of SQL Server.
/// Program.cs's normal seeding runs against it, so every factory starts with the
/// full demo world (restaurants, menus, users, orders).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"OrderOrange-Tests-{Guid.NewGuid():N}";

    /// <summary>
    /// Chat lives in a real MongoDB, so each factory gets its own throwaway database.
    /// Without this the tests would read and write the live "wajibat" conversations.
    /// </summary>
    private readonly string _mongoDb = $"wajibat-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The small hand-crafted world only — the big generated dataset would break
        // the deterministic counts these tests assert on.
        builder.UseSetting("Seed:Scale", "Demo");
        builder.UseSetting("Mongo:Database", _mongoDb);

        // The shipped appsettings restricts admin sign-in to the live owner's email.
        // Cleared here so tests run against behaviour, not production configuration —
        // the allowlist has its own tests that set it explicitly.
        builder.UseSetting("Admin:AllowedEmails", "");

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers the options both directly and via
            // IDbContextOptionsConfiguration — every trace of the SqlServer
            // registration must go, or EF sees two providers and refuses to run.
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));

            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }

    /// <summary>Drops this run's throwaway Mongo database so test data never accumulates.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                Services.GetRequiredService<IMongoClient>().DropDatabase(_mongoDb);
            }
            catch { /* mongod may already be gone; nothing to clean up then */ }
        }
        base.Dispose(disposing);
    }
}
