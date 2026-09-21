using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The public "Contact us" form and the admin inbox behind it. The form has no login and
/// no captcha yet, so two hard ceilings stand in for spam control: the whole platform
/// accepts <see cref="DailyLimit"/> messages a day, and one address at most <see cref="PerIpLimit"/>.
/// Both reset at local midnight. Raise them in appsettings (Contact:DailyLimit, Contact:PerIpLimit)
/// once a captcha is in place.
/// </summary>
[ApiController]
[Route("api/contact")]
public class ContactController(ContactStore store, IConfiguration config) : ApiControllerBase
{
    private int DailyLimit => int.TryParse(config["Contact:DailyLimit"], out var n) && n > 0 ? n : 100;
    private int PerIpLimit => int.TryParse(config["Contact:PerIpLimit"], out var n) && n > 0 ? n : 5;

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Send(ContactRequest req)
    {
        var email = (req.Email ?? "").Trim();
        var phone = (req.Phone ?? "").Trim();
        var title = (req.Title ?? "").Trim();
        var text = (req.Text ?? "").Trim();
        if (email.Length is < 5 or > 120 || !email.Contains('@') || !email.Contains('.'))
            return BadRequest(new { code = "email", message = "Please enter a valid email address." });
        if (phone.Length is < 6 or > 30)
            return BadRequest(new { code = "phone", message = "Please enter a phone number." });
        if (title.Length is < 2 or > 120)
            return BadRequest(new { code = "title", message = "Please give the message a title." });
        if (text.Length is < 5 or > 3000)
            return BadRequest(new { code = "text", message = "Please write a message (5 to 3000 characters)." });

        var todayStart = DateTime.Today;
        var ip = ClientIp();
        if (await store.CountSinceAsync(todayStart) >= DailyLimit)
            return StatusCode(429, new { code = "daily", message = "We have received the maximum number of messages for today. Please try again tomorrow or email info@orderorange.com." });
        if (ip.Length > 0 && await store.CountSinceFromAsync(todayStart, ip) >= PerIpLimit)
            return StatusCode(429, new { code = "ip", message = "You have sent several messages today. We will reply to those first; please try again tomorrow." });

        await store.AddAsync(new ContactMessageDoc
        {
            Email = email, Phone = phone, Title = title, Text = text,
            Source = (req.Source ?? "").Trim()[..Math.Min((req.Source ?? "").Trim().Length, 80)],
            Ip = ip, CreatedAt = DateTime.Now,
        });
        return Ok(new { ok = true });
    }

    // ---------- admin inbox ----------

    [HttpGet("admin")]
    [Authorize(Roles = "Administrator")]
    public Task<ContactPageDto> Inbox([FromQuery] int skip = 0, [FromQuery] int take = 50) =>
        store.PageAsync(Math.Max(0, skip), Math.Clamp(take, 1, 200), DailyLimit, DateTime.Today);

    [HttpGet("admin/summary")]
    [Authorize(Roles = "Administrator")]
    public Task<ContactSummaryDto> Summary() => store.SummaryAsync(DailyLimit, DateTime.Today);

    [HttpPost("admin/{id}/read")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> MarkRead(string id, [FromQuery] bool read = true) =>
        await store.MarkReadAsync(id, read) ? NoContent() : NotFound();

    [HttpDelete("admin/{id}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(string id) =>
        await store.DeleteAsync(id) ? NoContent() : NotFound();

    /// <summary>Visitor address: Cloudflare's header, then X-Forwarded-For, then the socket — trusted only from loopback callers (the proxy).</summary>
    private string ClientIp()
    {
        var socket = HttpContext.Connection.RemoteIpAddress;
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
}
