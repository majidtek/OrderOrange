using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>The Windows till's downloads: the one-file installer beside the update zip.</summary>
public class TillDownloadTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TillDownloadTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Setup_ServesTheInstallerTheManifestNames_AndTheZipStillServesUpdates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "till-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "OrderOrangeTill-Setup-1.2.0.exe"), [0x4D, 0x5A, 1, 2]);
            await File.WriteAllBytesAsync(Path.Combine(dir, "LocalHandler-1.2.0.zip"), [0x50, 0x4B, 3, 4]);
            await File.WriteAllTextAsync(Path.Combine(dir, "version.json"),
                """{ "version": "1.2.0", "file": "LocalHandler-1.2.0.zip", "sha256": "x", "notes": "n", "setup": "OrderOrangeTill-Setup-1.2.0.exe" }""");

            var client = _factory.WithWebHostBuilder(b => b.UseSetting("Till:Dir", dir)).CreateClient();

            var manifest = await client.GetFromJsonAsync<TillVersionDto>("api/till/version");
            Assert.Equal("OrderOrangeTill-Setup-1.2.0.exe", manifest!.Setup);

            var setup = await client.GetAsync("api/till/setup");
            Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
            Assert.Equal("OrderOrangeTill-Setup-1.2.0.exe", setup.Content.Headers.ContentDisposition?.FileNameStar ?? setup.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal([0x4D, 0x5A, 1, 2], await setup.Content.ReadAsByteArrayAsync());

            var zip = await client.GetAsync("api/till/download");
            Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
            Assert.Equal([0x50, 0x4B, 3, 4], await zip.Content.ReadAsByteArrayAsync());

            // A release published before installers existed has no "setup": that download is simply absent.
            await File.WriteAllTextAsync(Path.Combine(dir, "version.json"),
                """{ "version": "1.1.17", "file": "LocalHandler-1.2.0.zip" }""");
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("api/till/setup")).StatusCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
