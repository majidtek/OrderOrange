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

    [HttpGet("version")]
    public IActionResult Version()
    {
        var path = Path.Combine(Dir, "version.json");
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/json");
    }

    [HttpGet("download")]
    public IActionResult Download()
    {
        var manifest = Path.Combine(Dir, "version.json");
        if (!System.IO.File.Exists(manifest)) return NotFound();
        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifest));
        var file = doc.RootElement.TryGetProperty("file", out var f) ? f.GetString() : null;
        if (string.IsNullOrEmpty(file) || file.Contains("..") || file.Contains('/') || file.Contains('\\')) return NotFound();
        var path = Path.Combine(Dir, file);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/zip", file, enableRangeProcessing: true);
    }
}
