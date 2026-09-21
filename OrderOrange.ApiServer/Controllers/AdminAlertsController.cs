using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The administrators' bell: who joined the platform since they last looked. Written by
/// <see cref="AdminAlerts"/> from the registration endpoints, read only here.
/// </summary>
[ApiController]
[Route("api/admin/alerts")]
[Authorize(Roles = "Administrator")]
public class AdminAlertsController(AdminAlertStore store) : ApiControllerBase
{
    /// <summary>The recent alerts, newest first, with this administrator's unread count.</summary>
    [HttpGet]
    public async Task<ActionResult<AdminAlertsDto>> Recent([FromQuery] int limit = 50)
    {
        var me = CurrentUserId;
        var docs = await store.RecentAsync(Math.Clamp(limit, 1, 200));
        var items = docs.Select(d => new AdminAlertDto(
            d.Id, d.Kind, d.Title, d.Body, d.Url, d.CreatedAt, d.ReadBy.Contains(me))).ToList();
        return Ok(new AdminAlertsDto(items, items.Count(i => !i.Read)));
    }

    /// <summary>Just the badge number — what the bell polls for, so the list is not re-sent.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<AdminAlertsDto>> Summary()
    {
        var unread = await store.UnreadCountAsync(CurrentUserId);
        var latest = await store.RecentAsync(1);
        var items = latest.Select(d => new AdminAlertDto(
            d.Id, d.Kind, d.Title, d.Body, d.Url, d.CreatedAt, d.ReadBy.Contains(CurrentUserId))).ToList();
        return Ok(new AdminAlertsDto(items, (int)unread));
    }

    /// <summary>Opening the bell clears it, for this administrator only.</summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead()
    {
        await store.MarkAllReadAsync(CurrentUserId);
        return Ok();
    }
}
