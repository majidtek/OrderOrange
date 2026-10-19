using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>Admin → Disk space: drives, folder sizes and the fixed set of cleanups.</summary>
[ApiController]
[Route("api/admin/disk")]
[Authorize(Roles = "Administrator")]
public sealed class AdminDiskController(DiskManager disk) : ControllerBase
{
    [HttpGet]
    public async Task<DiskStatusDto> Get()
    {
        if (disk.Last is null && !disk.Scanning) _ = disk.ScanAsync();   // first visit starts a scan
        return new DiskStatusDto(DiskManager.Drives(), disk.Last, disk.Scanning, await disk.HistoryAsync());
    }

    [HttpPost("scan")]
    public IActionResult Scan()
    {
        if (disk.Scanning) return Conflict(new { message = "A scan is already running." });
        _ = disk.ScanAsync();
        return Accepted();
    }

    [HttpPost("clean")]
    public async Task<IActionResult> Clean(DiskCleanRequest req)
    {
        var by = User.Identity?.Name ?? User.FindFirst("email")?.Value ?? "admin";
        var (files, bytes, error) = await disk.CleanAsync(req.Category ?? "", req.Value, by);
        return error is null ? Ok(new DiskCleanResultDto(files, bytes)) : BadRequest(new { message = error });
    }
}