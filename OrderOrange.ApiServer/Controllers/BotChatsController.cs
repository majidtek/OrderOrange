using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
public class BotChatsController(BotChatStore bots) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BotChatIpsDto>> Ips(
        [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? search = null)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await bots.ByIpSummaryAsync(Math.Max(0, skip), take, search);
        var total = await bots.IpCountAsync(search);
        return Ok(new BotChatIpsDto(
            rows.Select(r => new BotChatIpDto(r.Ip, r.Turns, r.Sessions, r.First, r.Last, r.LastText, r.Country)).ToList(),
            total));
    }

    /// <summary>Everything one address ever typed, newest first.</summary>
    [HttpGet("turns")]
    public async Task<ActionResult<BotChatTurnsDto>> Turns([FromQuery] string ip = "", [FromQuery] int limit = 500)
    {
        var docs = await bots.ByIpAsync(ip, Math.Clamp(limit, 1, 2000));
        return Ok(new BotChatTurnsDto(ip, docs.Select(d => new BotChatTurnDto(
            d.Id, d.Session, d.Bot, d.Text, d.Reply, d.Page, d.Lang, d.StoreId, d.UserName, d.At)).ToList()));
    }
}
