using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Per-order chat between the customer, the restaurant and the rider. The messages
/// live in MongoDB (see <see cref="ChatStore"/>); SQL is used only to check that the
/// caller is actually part of this order.
///
/// Anti-abuse: per-user rate limiting, strict size caps, and an attachment
/// type whitelist — a hostile user can't flood the server or the database.
/// </summary>
public class ChatController(AppDbContext db, ChatStore chat, IMemoryCache cache) : ApiControllerBase
{
    private const int MaxMessagesPerMinute = 12;
    private const int MaxTextLength = 1000;
    private const int MaxAttachmentChars = 2_800_000; // ≈ 2 MB of binary as base64
    private const int MaxMessagesPerOrder = 300;

    private static readonly HashSet<string> AllowedTypes = ["image", "file", "voice", "location"];

    [HttpGet("{orderId:int}")]
    public async Task<ActionResult<List<ChatMessageDto>>> Messages(int orderId, int afterId = 0)
    {
        if (!await MayAccess(orderId)) return Forbid();
        var messages = await chat.MessagesAsync(orderId, afterId);
        return messages.Select(m => m.ToDto()).ToList();
    }

    [HttpPost("{orderId:int}")]
    public async Task<IActionResult> Send(int orderId, SendChatRequest req)
    {
        if (!await MayAccess(orderId)) return Forbid();

        // Fixed-window rate limit per user: flooding gets a 429, not a database.
        var key = $"chat-rate-{CurrentUserId}";
        var count = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return 0;
        });
        if (count >= MaxMessagesPerMinute)
            return StatusCode(429, new { message = "You're sending messages too quickly — wait a moment." });
        cache.Set(key, count + 1, TimeSpan.FromMinutes(1));

        var text = req.Text?.Trim();
        var hasAttachment = !string.IsNullOrWhiteSpace(req.AttachmentData) || req.AttachmentType == "location";
        if (string.IsNullOrWhiteSpace(text) && !hasAttachment)
            return BadRequest(new { message = "The message is empty." });
        if (text?.Length > MaxTextLength)
            return BadRequest(new { message = $"Messages are limited to {MaxTextLength} characters." });

        if (req.AttachmentType is not null)
        {
            if (!AllowedTypes.Contains(req.AttachmentType))
                return BadRequest(new { message = "Attachment type not allowed." });
            if (req.AttachmentType != "location")
            {
                if (string.IsNullOrWhiteSpace(req.AttachmentData) || !req.AttachmentData.StartsWith("data:"))
                    return BadRequest(new { message = "Invalid attachment." });
                if (req.AttachmentData.Length > MaxAttachmentChars)
                    return BadRequest(new { message = "Attachments are limited to 2 MB." });
            }
        }

        if (await chat.CountForOrderAsync(orderId) >= MaxMessagesPerOrder)
            return BadRequest(new { message = "This conversation is full." });

        var role = Enum.TryParse<UserRole>(User.FindFirstValue(ClaimTypes.Role), out var parsed)
            ? parsed : UserRole.Customer;

        await chat.AddAsync(new ChatDoc
        {
            OrderId = orderId,
            SenderUserId = CurrentUserId,
            SenderName = CurrentUserName,
            SenderRole = role,
            Text = text,
            AttachmentData = req.AttachmentType == "location" ? null : req.AttachmentData,
            AttachmentType = req.AttachmentType,
            FileName = req.FileName?.Trim(),
            At = DateTime.Now
        });
        return NoContent();
    }

    /// <summary>Only the order's customer, restaurant, rider, or an admin may read/write.</summary>
    private async Task<bool> MayAccess(int orderId)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return false;
        return IsAdmin
            || order.CustomerId == CurrentUserId
            || order.DriverUserId == CurrentUserId
            || (CurrentRestaurantId != 0 && order.RestaurantId == CurrentRestaurantId);
    }
}
