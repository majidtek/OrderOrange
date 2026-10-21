using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The assistant report: which addresses talked to the bot, and what they said. One row per
/// address on the front page, the whole conversation behind it.
/// </summary>
[ApiController]
[Route("api/admin/bot-chats")]
[Authorize(Roles = "Administrator")]
public class BotChatsController(BotChatStore bots, AppDbContext db) : ApiControllerBase
{
    /// <param name="who">"partner" (the partner portal and its POS assistant), "customer", or empty for everyone.</param>
    [HttpGet]
    public async Task<ActionResult<BotChatIpsDto>> Ips(
        [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? search = null, [FromQuery] string? who = null)
    {
        take = Math.Clamp(take, 1, 200);
        who = who is "partner" or "customer" ? who : null;
        var rows = await bots.ByIpSummaryAsync(Math.Max(0, skip), take, search, who);
        var total = await bots.IpCountAsync(search, who);
        var names = await StoreNamesAsync(rows.Select(r => r.StoreId));
        return Ok(new BotChatIpsDto(
            rows.Select(r => new BotChatIpDto(r.Ip, r.Turns, r.Sessions, r.First, r.Last, r.LastText, r.Country,
                r.Bots, r.UserName, r.StoreId is { } id ? names.GetValueOrDefault(id) : null)).ToList(),
            total));
    }

    /// <summary>Business names for the store ids the partner and POS assistants logged, so the report says who asked.</summary>
    private async Task<Dictionary<int, string>> StoreNamesAsync(IEnumerable<int?> storeIds)
    {
        var ids = storeIds.OfType<int>().Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Restaurants.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);
    }

    /// <summary>Everything one address ever typed, newest first.</summary>
    [HttpGet("turns")]
    public async Task<ActionResult<BotChatTurnsDto>> Turns([FromQuery] string ip = "", [FromQuery] int limit = 500)
    {
        var docs = await bots.ByIpAsync(ip, Math.Clamp(limit, 1, 2000));
        var names = await StoreNamesAsync(docs.Select(d => d.StoreId));
        return Ok(new BotChatTurnsDto(ip, docs.Select(d => new BotChatTurnDto(
            d.Id, d.Session, d.Bot, d.Text, d.Reply, d.Page, d.Lang, d.StoreId, d.UserName, d.At,
            d.StoreId is { } id ? names.GetValueOrDefault(id) : null)).ToList()));
    }
}
