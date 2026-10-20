using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The shop till (LocalHandler) keeps itself current from here. A folder on the server
/// (<c>Till:Dir</c>, default <c>&lt;content root&gt;/till</c>) holds the latest build as a zip
/// plus <c>version.json</c> — <c>{ "version", "file", "sha256", "notes" }</c>. Drop a new
/// pair in and every till picks it up on its next check; nothing else to deploy.
/// </summary>
[ApiController]
[Route("api/till")]
[AllowAnonymous]
public class TillController(IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    private string Dir => config["Till:Dir"] ?? Path.Combine(env.ContentRootPath, "till");

    // Both manifests are tiny and change exactly when a release lands — a browser must never
    // show yesterday's version (Majed 2026-10-18: partner Settings still said 1.0.0 after 1.2.0
    // was published; without Cache-Control the browser kept the old JSON by heuristic).
    [HttpGet("version")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Version()
    {
        var path = Path.Combine(Dir, "version.json");
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/json");
    }

    [HttpGet("download")]
    public IActionResult Download() => Serve("version.json", "application/zip");

    // ---------- The Android till (OrderOrange.AndroidTill) ----------
    // Same folder, its own manifest: android.json → { "version", "file", "sha256", "notes" }
    // next to the APK. The partner Settings page links here; the terminal side-loads it.

    [HttpGet("android/version")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult AndroidVersion()
    {
        var path = Path.Combine(Dir, "android.json");
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/json");
    }

    [HttpGet("android/download")]
    public IActionResult AndroidDownload() => Serve("android.json", "application/vnd.android.package-archive");

    // ---------- The OrderOrange Partner app (OrderOrange.PartnerApp) ----------
    // The partner portal as an app with its printers built in, for Android and Windows; it
    // replaces the separate Android till in partner Settings. Same folder, one manifest per
    // platform: partner-android.json (APK) and partner-windows.json (zip), written by
    // tools/publish-partner-app.ps1.

    [HttpGet("partner/{platform}/version")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult PartnerAppVersion(string platform)
    {
        if (PartnerManifest(platform) is not { } name) return NotFound();
        var path = Path.Combine(Dir, name);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/json");
    }

    [HttpGet("partner/{platform}/download")]
    public IActionResult PartnerAppDownload(string platform) => PartnerManifest(platform) is { } name
        ? Serve(name, platform == "android" ? "application/vnd.android.package-archive" : "application/zip")
        : NotFound();

    /// <summary>Only the two known platforms map to a manifest — never a name of the caller's choosing.</summary>
    private static string? PartnerManifest(string platform) => platform switch
    {
        "android" => "partner-android.json",
        "windows" => "partner-windows.json",
        _ => null,
    };

    /// <summary>The file a manifest names, from the till folder only — never a path of the caller's choosing.</summary>
    private IActionResult Serve(string manifestName, string contentType)
    {
        var manifest = Path.Combine(Dir, manifestName);
        if (!System.IO.File.Exists(manifest)) return NotFound();
        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifest));
        var file = doc.RootElement.TryGetProperty("file", out var f) ? f.GetString() : null;
        if (string.IsNullOrEmpty(file) || file.Contains("..") || file.Contains('/') || file.Contains('\\')) return NotFound();
        var path = Path.Combine(Dir, file);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, contentType, file, enableRangeProcessing: true);
    }
}
