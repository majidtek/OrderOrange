using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MongoDB.Driver;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>Admin Settings → Backups: SQL + Mongo to a folder, every N hours.</summary>
[ApiController]
[Route("api/admin/backup")]
[Authorize(Roles = "Administrator")]
public sealed class AdminBackupController(BackupRunner runner, IConfiguration config, IMongoDatabase mongo) : ControllerBase
{
    // ---------- Backups page (Majed 2026-10-14: "in admin panel add page, I can do backup any time I want") ----------

    /// <summary>Every backup zip in the folder, newest first.</summary>
    [HttpGet("files")]
    public async Task<List<BackupFileDto>> Files()
    {
        var s = await runner.GetAsync();
        if (string.IsNullOrWhiteSpace(s.Folder) || !Directory.Exists(s.Folder)) return [];
        return new DirectoryInfo(s.Folder).GetFiles("*.zip")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new BackupFileDto(f.Name, f.Length, f.LastWriteTime,
                f.Name.EndsWith("_full.zip", StringComparison.OrdinalIgnoreCase) ? "full" : f.Name.EndsWith("_diff.zip", StringComparison.OrdinalIgnoreCase) ? "diff" : ""))
            .ToList();
    }

    /// <summary>A download link valid for 10 minutes, so the browser can fetch a multi-GB zip itself.</summary>
    [HttpPost("files/{name}/link")]
    public async Task<IActionResult> Link(string name)
    {
        var s = await runner.GetAsync();
        if (SafeFile(s.Folder, name) is null) return NotFound();
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return Ok(new { url = $"api/admin/backup/download/{Uri.EscapeDataString(name)}?exp={exp}&sig={Sign(name, exp)}" });
    }

    /// <summary>Streams one backup zip; the signed link stands in for the sign-in.</summary>
    [HttpGet("download/{name}")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(string name, long exp, string sig)
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return StatusCode(410, "This download link has expired. Open the Backups page again.");
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(Sign(name, exp)), System.Text.Encoding.ASCII.GetBytes(sig ?? "")))
            return Forbid();
        var s = await runner.GetAsync();
        var path = SafeFile(s.Folder, name);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/zip", name, enableRangeProcessing: true);
    }

    /// <summary>Only a plain *.zip name directly inside the backup folder — never a path.</summary>
    private static string? SafeFile(string? folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name)) return null;
        if (name != Path.GetFileName(name) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        var full = Path.GetFullPath(Path.Combine(folder, name));
        return Path.GetDirectoryName(full)!.Equals(Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(full) ? full : null;
    }

    private string Sign(string name, long exp)
    {
        var key = System.Text.Encoding.UTF8.GetBytes("backup-download:" + (config["Jwt:Key"] ?? ""));
        using var h = new System.Security.Cryptography.HMACSHA256(key);
        return Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{name}|{exp}")))[..32].ToLowerInvariant();
    }

    [HttpGet]
    public async Task<BackupStatusDto> Get()
    {
        var s = await runner.GetAsync();
        var (files, bytes, free) = BackupRunner.FolderStats(s.Folder);
        DateTime? next = s.Enabled && s.Folder.Length > 0
            ? (s.LastRunAt ?? DateTime.Now).AddHours(s.LastRunAt is null ? 0 : Math.Max(1, s.IntervalHours))
            : null;
        var sqlDb = "";
        try { sqlDb = new SqlConnectionStringBuilder(config.GetConnectionString("Default")).InitialCatalog; } catch { }
        return new BackupStatusDto(s.Enabled, s.Folder, s.IntervalHours, s.KeepHours,
            runner.Running, runner.Step,
            s.LastRunAt, s.LastFullAt, s.LastOk, s.LastMessage,
            s.LastZipFile, s.LastZipBytes, s.LastSqlBytes, s.LastMongoBytes, s.LastSeconds,
            next, files, bytes, free, sqlDb, mongo.DatabaseNamespace.DatabaseName);
    }

    [HttpPut]
    public async Task<IActionResult> Save(BackupSettingsRequest req)
    {
        var folder = (req.Folder ?? "").Trim();
        if (req.Enabled && folder.Length == 0) return BadRequest(new { message = "Enter a backup folder first." });
        if (folder.Length > 0)
        {
            if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\"))
                return BadRequest(new { message = @"Use a full local path such as C:\OrderOrangeBackups." });
            try
            {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".write-test");
                await System.IO.File.WriteAllTextAsync(probe, "ok");
                System.IO.File.Delete(probe);
            }
            catch (Exception ex) { return BadRequest(new { message = $"The server cannot write to that folder: {ex.Message}" }); }
        }
        int[] intervals = [1, 2, 3, 6, 12, 24];
        int[] keeps = [6, 12, 24, 48, 72, 168];
        var interval = intervals.Contains(req.IntervalHours) ? req.IntervalHours : 1;
        var keep = keeps.Contains(req.KeepHours) ? req.KeepHours : 24;
        await runner.SaveSettingsAsync(req.Enabled, folder, interval, keep);
        return NoContent();
    }

    /// <summary>Starts a backup now in the background; the page polls GET for the result.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunNow()
    {
        var s = await runner.GetAsync();
        if (string.IsNullOrWhiteSpace(s.Folder)) return BadRequest(new { message = "Enter and save a backup folder first." });
        if (runner.Running) return Conflict(new { message = "A backup is already running." });
        _ = Task.Run(() => runner.RunAsync());
        return Accepted();
    }
}