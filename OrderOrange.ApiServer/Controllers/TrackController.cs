using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>Page-visit tracking — each app pings this as the signed-in user navigates.</summary>
public class TrackController(AppDbContext db, VisitStore visits, IMemoryCache cache) : ApiControllerBase
{
    /// <summary>
    /// A page was opened. Guests count: nearly everyone browsing the customer app has
    /// not signed in, and a visitor board that only showed accounts would show almost
    /// nothing. A guest is stored with no user id and no name — the address is all the
    /// identity there is.
    /// </summary>
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Visit(TrackRequest req)
    {
        var signedIn = User.Identity?.IsAuthenticated == true;
        var role = signedIn && Enum.TryParse<UserRole>(User.FindFirstValue(ClaimTypes.Role), out var parsed)
            ? parsed
            : UserRole.Customer;

        var app = (req.App ?? "").Trim();
        var page = string.IsNullOrWhiteSpace(req.Page) ? "/" : req.Page.Trim();
        if (page.Length > 300) page = page[..300];
        var ip = VisitorIp(req.Ip);

        // Blazor re-raises navigation on reconnects and re-renders, and an open endpoint
        // can be called in a loop. One row per address, app and page every half minute is
        // plenty to see where people go, and keeps the table from being flooded.
        var recent = $"visit:{ip}|{app}|{page}";
        if (cache.TryGetValue(recent, out _)) return NoContent();
        cache.Set(recent, true, TimeSpan.FromSeconds(30));

        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = signedIn ? CurrentUserId : 0,
            UserName = signedIn ? CurrentUserName : "",
            Role = role,
            App = app,
            Page = page,
            Ip = ip,
            At = DateTime.Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// What a customer looked for, and how much came back. Anonymous on purpose —
    /// most searching happens before anyone signs in, and those are exactly the
    /// terms worth knowing about.
    /// </summary>
    [HttpPost("search")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> Search(TrackSearchRequest req)
    {
        var term = (req.Term ?? "").Trim();
        if (term.Length is < 2 or > 120) return NoContent();

        db.SearchLogs.Add(new SearchLog
        {
            UserId = User.Identity?.IsAuthenticated == true ? CurrentUserId : 0,
            UserName = User.Identity?.IsAuthenticated == true ? CurrentUserName : "",
            Term = term,
            Results = Math.Max(0, req.Results),
            App = string.IsNullOrWhiteSpace(req.App) ? "Customer" : req.App.Trim(),
            Locale = (req.Locale ?? "").Trim(),
            Ip = ClientIp(),
            At = DateTime.Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// The VISITOR's address. Each app is Blazor Server, so the ping arrives from the web
    /// server rather than the browser and <see cref="ClientIp"/> would return this machine
    /// for everybody. The app therefore reports the address it saw — but only a caller on
    /// the same box is believed, because the endpoint is open and a stranger must not be
    /// able to write whatever address they like into the log.
    /// </summary>
    private string VisitorIp(string? reported)
    {
        var caller = HttpContext.Connection.RemoteIpAddress;
        var fromOurOwnServer = caller is null || System.Net.IPAddress.IsLoopback(caller);
        if (fromOurOwnServer && !string.IsNullOrWhiteSpace(reported))
            return reported.Trim()[..Math.Min(reported.Trim().Length, 60)];

        // Whatever is left, refuse to call a loopback address a visitor. An app that did
        // not report one leaves only the address of this machine, and recording that put
        // 127.0.0.1 at the top of the visitor board as a single "person" who was in fact
        // everybody. Blank instead: the board skips it rather than inventing a visitor.
        var seen = ClientIp();
        return System.Net.IPAddress.TryParse(seen, out var parsed) && System.Net.IPAddress.IsLoopback(parsed)
            ? ""
            : seen;
    }

    /// <summary>
    /// The caller's address. The browser apps reach us through Cloudflare and then the
    /// api.orderorange.com proxy: Cloudflare puts the visitor in CF-Connecting-IP, and the
    /// proxy overwrites X-Forwarded-For with Cloudflare's own edge address — so the
    /// Cloudflare header wins, then X-Forwarded-For (FIRST entry is the client, the rest
    /// are proxies), then the socket.
    /// </summary>
    private string ClientIp()
    {
        var socket = HttpContext.Connection.RemoteIpAddress;
        // Forwarding headers are believed only from our own box: the api.orderorange.com
        // proxy and the server-rendered customer site both call from loopback. A browser
        // hitting the API straight (staging) is its own address and gets no say.
        if (socket is null || System.Net.IPAddress.IsLoopback(socket))
        {
            foreach (var header in new[] { "CF-Connecting-IP", "X-Client-IP", "X-Forwarded-For" })
            {
                var first = Request.Headers[header].ToString().Split(',')[0].Trim();
                if (first.Length > 0 && !(System.Net.IPAddress.TryParse(first, out var h) && System.Net.IPAddress.IsLoopback(h)))
                    return first[..Math.Min(first.Length, 60)];
            }
        }
        return socket?.ToString() ?? "";
    }

    /// <summary>
    /// One exchange with an assistant: what the visitor typed and what came back. Anonymous,
    /// because almost nobody asking the assistant has signed in — and those questions are
    /// exactly the ones worth reading. The address is resolved the same way a page visit is.
    /// </summary>
    [HttpPost("bot")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> BotChat(BotChatLogRequest req, [FromServices] Services.BotChatStore bots)
    {
        var text = (req.Text ?? "").Trim();
        if (text.Length is < 1 or > 2000) return NoContent();

        var session = (req.Session ?? "").Trim();
        if (session.Length is 0 or > 64) session = Guid.NewGuid().ToString("N");

        // The same guard the page visit uses: an open endpoint must not become a firehose.
        var gate = $"bot:{session}";
        if (cache.TryGetValue(gate, out _)) return NoContent();
        cache.Set(gate, true, TimeSpan.FromMilliseconds(600));

        // Same rule the visitor board uses: this machine is not a visitor. A turn we cannot
        // place is filed with a blank address rather than piling everyone onto 127.0.0.1.
        var ip = VisitorIp(req.Ip);
        if (System.Net.IPAddress.TryParse(ip, out var parsed) && System.Net.IPAddress.IsLoopback(parsed))
            ip = "";

        var signedIn = User.Identity?.IsAuthenticated == true;
        await bots.AddAsync(new Services.BotChatDoc
        {
            Ip = ip,
            Session = session,
            Bot = string.IsNullOrWhiteSpace(req.Bot) ? "customer" : req.Bot.Trim()[..Math.Min(req.Bot.Trim().Length, 20)],
            Text = text,
            Reply = Cap(req.Reply, 2000),
            Page = Cap(req.Page, 300),
            Lang = Cap(req.Lang, 10),
            StoreId = req.StoreId,
            UserId = signedIn ? CurrentUserId : null,
            UserName = signedIn ? CurrentUserName : null,
            Country = Cap(Request.Headers["CF-IPCountry"].ToString(), 4),
            Agent = Cap(Request.Headers.UserAgent.ToString(), 200),
        });
        return NoContent();
    }

    private static string? Cap(string? text, int max)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return null;
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Anonymous store/product view for partner analytics — no user identity is stored.</summary>
    [HttpPost("store-visit")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> StoreVisit(TrackStoreVisitRequest req)
    {
        if (req.RestaurantId <= 0) return NoContent();
        db.StoreVisits.Add(new Models.StoreVisit
        {
            RestaurantId = req.RestaurantId,
            MenuItemId = req.ItemId,
            ItemName = string.IsNullOrWhiteSpace(req.ItemName) ? null : req.ItemName.Trim()[..Math.Min(req.ItemName.Trim().Length, 150)],
            At = DateTime.Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }
}
