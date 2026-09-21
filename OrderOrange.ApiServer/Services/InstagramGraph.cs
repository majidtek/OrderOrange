using System.Text.Json;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The Instagram Graph API as a shop uses it — the Instagram-login flavour on
/// graph.instagram.com, where the owner's own long-lived token is the whole credential and
/// OrderOrange needs no Facebook app of its own.
///
/// Publishing is two steps by Instagram's design: a container is created from a PUBLIC
/// image URL, Instagram downloads and checks it, and only then may it be published. That
/// is why the picture has to be reachable on the internet (api.orderorange.com serves it).
/// </summary>
public sealed class InstagramGraph(IHttpClientFactory factory, ILogger<InstagramGraph> log)
{
    private const string Base = "https://graph.instagram.com/v21.0";
    private HttpClient Http() => factory.CreateClient("ig");

    public sealed record Me(string UserId, string Username, string AccountType);

    /// <summary>Who does this token belong to? Also the check that a pasted token is real.</summary>
    public async Task<Me> MeAsync(string token)
    {
        var json = await GetAsync($"{Base}/me?fields=user_id,username,account_type&access_token={Uri.EscapeDataString(token)}");
        // user_id is the numeric id publishing needs; id is the app-scoped one.
        var userId = json.TryGetProperty("user_id", out var u) ? u.ToString() : json.GetProperty("id").ToString();
        return new Me(userId,
            json.TryGetProperty("username", out var n) ? n.GetString() ?? "" : "",
            json.TryGetProperty("account_type", out var t) ? t.GetString() ?? "" : "");
    }

    /// <summary>Posts left today: Instagram allows 50 in a rolling 24 hours.</summary>
    public async Task<(int used, int total)> QuotaAsync(string userId, string token)
    {
        try
        {
            var json = await GetAsync($"{Base}/{userId}/content_publishing_limit?fields=quota_usage,config&access_token={Uri.EscapeDataString(token)}");
            var d = json.GetProperty("data")[0];
            var used = d.GetProperty("quota_usage").GetInt32();
            var total = d.TryGetProperty("config", out var c) && c.TryGetProperty("quota_total", out var q) ? q.GetInt32() : 50;
            return (used, total);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Instagram quota unavailable");
            return (0, 50);
        }
    }

    /// <summary>
    /// A long-lived token can be swapped for a fresh 60-day one, but only after it is a day
    /// old. Called when fewer than 15 days remain, so a store that keeps posting never has
    /// to paste a token again.
    /// </summary>
    public async Task<(string token, DateTime expiresAt)> RefreshAsync(string token)
    {
        var json = await GetAsync($"{Base}/refresh_access_token?grant_type=ig_refresh_token&access_token={Uri.EscapeDataString(token)}");
        var seconds = json.GetProperty("expires_in").GetInt64();
        return (json.GetProperty("access_token").GetString()!, DateTime.UtcNow.AddSeconds(seconds));
    }

    /// <summary>Create the container, wait for Instagram to accept the picture, publish it.</summary>
    public async Task<string> PublishImageAsync(string userId, string token, string imageUrl, string caption)
    {
        var create = await PostAsync($"{Base}/{userId}/media", new Dictionary<string, string>
        {
            ["image_url"] = imageUrl,
            ["caption"] = caption,
            ["access_token"] = token,
        });
        var containerId = create.GetProperty("id").GetString()!;
        log.LogInformation("Instagram container {Container} for {User}", containerId, userId);

        await WaitUntilReadyAsync(token, containerId);

        var publish = await PostAsync($"{Base}/{userId}/media_publish", new Dictionary<string, string>
        {
            ["creation_id"] = containerId,
            ["access_token"] = token,
        });
        return publish.GetProperty("id").GetString()!;
    }

    public async Task<string> PermalinkAsync(string mediaId, string token)
    {
        try
        {
            var json = await GetAsync($"{Base}/{mediaId}?fields=permalink&access_token={Uri.EscapeDataString(token)}");
            return json.TryGetProperty("permalink", out var p) ? p.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    /// <summary>The account's own recent posts, to show the owner what is already up.</summary>
    public async Task<List<Shared.InstagramMediaDto>> RecentAsync(string userId, string token, int take = 6)
    {
        var list = new List<Shared.InstagramMediaDto>();
        try
        {
            var json = await GetAsync($"{Base}/{userId}/media?fields=id,permalink,media_url,thumbnail_url,caption,timestamp&limit={take}&access_token={Uri.EscapeDataString(token)}");
            foreach (var m in json.GetProperty("data").EnumerateArray())
            {
                DateTime? at = m.TryGetProperty("timestamp", out var ts) && DateTime.TryParse(ts.GetString(), out var parsed) ? parsed.ToLocalTime() : null;
                list.Add(new Shared.InstagramMediaDto(
                    m.GetProperty("id").GetString() ?? "",
                    m.TryGetProperty("permalink", out var p) ? p.GetString() ?? "" : "",
                    m.TryGetProperty("thumbnail_url", out var th) ? th.GetString() : m.TryGetProperty("media_url", out var mu) ? mu.GetString() : null,
                    m.TryGetProperty("caption", out var c) ? c.GetString() : null, at));
            }
        }
        catch (Exception ex) { log.LogDebug(ex, "Instagram recent media unavailable"); }
        return list;
    }

    private async Task WaitUntilReadyAsync(string token, string containerId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var json = await GetAsync($"{Base}/{containerId}?fields=status_code,status&access_token={Uri.EscapeDataString(token)}");
            var status = json.TryGetProperty("status_code", out var s) ? s.GetString() : null;
            if (status == "FINISHED") return;
            if (status is "ERROR" or "EXPIRED")
                throw new InvalidOperationException(json.TryGetProperty("status", out var detail) ? detail.GetString() ?? "Instagram rejected the picture." : "Instagram rejected the picture.");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        throw new TimeoutException("Instagram did not finish preparing the picture in a minute.");
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        using var resp = await Http().GetAsync(url);
        return await ReadAsync(resp);
    }

    private async Task<JsonElement> PostAsync(string url, Dictionary<string, string> form)
    {
        using var resp = await Http().PostAsync(url, new FormUrlEncodedContent(form));
        return await ReadAsync(resp);
    }

    /// <summary>Graph errors arrive as JSON; the message inside is what the owner needs to read.</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode) return JsonDocument.Parse(body).RootElement.Clone();
        string message = body;
        try
        {
            var err = JsonDocument.Parse(body).RootElement.GetProperty("error");
            message = err.TryGetProperty("error_user_msg", out var friendly) ? friendly.GetString() ?? ""
                    : err.TryGetProperty("message", out var m) ? m.GetString() ?? "" : body;
        }
        catch { /* not JSON — keep the raw body */ }
        throw new InstagramException(message);
    }
}

/// <summary>An error Instagram itself reported, worded for the person who will read it.</summary>
public sealed class InstagramException(string message) : Exception(message);
