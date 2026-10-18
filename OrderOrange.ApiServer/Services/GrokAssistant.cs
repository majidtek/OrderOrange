using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The language model behind the customer assistant — xAI's Grok through its Responses
/// API. The key lives only in the API's appsettings (<c>Grok:ApiKey</c>); the browser
/// apps never see it. No key = <see cref="Enabled"/> false and the rule-based bot keeps
/// answering alone, exactly as before.
/// </summary>
public sealed class GrokAssistant(IHttpClientFactory factory, IConfiguration config, ILogger<GrokAssistant> log)
{
    private string ApiKey => (config["Grok:ApiKey"] ?? "").Trim();
    private string Model => string.IsNullOrWhiteSpace(config["Grok:Model"]) ? "grok-4.7" : config["Grok:Model"]!.Trim();
    private string Endpoint => string.IsNullOrWhiteSpace(config["Grok:Endpoint"]) ? "https://api.x.ai/v1/responses" : config["Grok:Endpoint"]!.Trim();

    public bool Enabled => ApiKey.Length > 0;
    public string ModelName => Model;

    /// <summary>
    /// One answer. <paramref name="system"/> is everything the model must know about the
    /// platform and the store; <paramref name="history"/> the last few lines of the chat;
    /// <paramref name="text"/> what the customer just typed. Null = Grok could not answer
    /// (network, quota, malformed reply) and the caller should fall back.
    /// </summary>
    public async Task<string?> ReplyAsync(string system, IReadOnlyList<AssistantTurnDto> history, string text, CancellationToken ct = default, string? volatileContext = null)
    {
        if (!Enabled) return null;

        // Cost: xAI caches the longest identical prefix of the input. The big block (rules,
        // site guide, the store's menu) is sent first and never changes between turns; what
        // changes every turn (clock, step, basket, list on screen) rides in a SECOND short
        // system message after it, so the prefix stays cacheable — 2026-10-01 the uncached
        // prompt text was two thirds of the day's bill.
        var input = new List<object> { new { role = "system", content = system } };
        if (!string.IsNullOrWhiteSpace(volatileContext)) input.Add(new { role = "system", content = volatileContext });
        foreach (var turn in history)
        {
            var role = turn.Role == "assistant" ? "assistant" : "user";
            if (!string.IsNullOrWhiteSpace(turn.Text)) input.Add(new { role, content = turn.Text });
        }
        input.Add(new { role = "user", content = text });

        var body = new
        {
            model = Model,
            input,
            // A chat bubble, not an essay: the system prompt asks for short answers too,
            // but the cap is what makes the bill predictable.
            max_output_tokens = 400,
            temperature = 0.4,
            // A chat answer must come back in a second or two; the model's deep thinking
            // mode took eight seconds for a menu question and added nothing.
            reasoning = new { effort = string.IsNullOrWhiteSpace(config["Grok:Reasoning"]) ? "low" : config["Grok:Reasoning"]!.Trim() },
        };

        try
        {
            var http = factory.CreateClient("grok");
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Grok {Status}: {Body}", (int)res.StatusCode, json.Length > 500 ? json[..500] : json);
                return null;
            }
            var reply = ExtractText(json);
            if (string.IsNullOrWhiteSpace(reply))
            {
                log.LogWarning("Grok answered with no text: {Body}", json.Length > 500 ? json[..500] : json);
                return null;
            }
            return reply.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "Grok unreachable");
            return null;
        }
    }

    /// <summary>One tool the model used while answering an analyst question.</summary>
    public sealed record ToolCall(string Server, string Tool, string Arguments, string Output, bool Error);

    /// <summary>
    /// The analyst: a question answered WITH the OrderOrange MCP server as a remote tool,
    /// so the model reads live data (sales, menus, orders…) instead of guessing. xAI
    /// calls the MCP server itself, hence the public URL and the bearer token.
    /// </summary>
    public async Task<(string? Text, List<ToolCall> Calls, string? Error)> AnalystAsync(
        string system, IReadOnlyList<AssistantTurnDto> history, string question, string mcpUrl, string mcpToken, CancellationToken ct = default, string? reasoning = null)
    {
        if (!Enabled) return (null, [], "Grok is not configured.");
        var input = new List<object> { new { role = "system", content = system } };
        foreach (var turn in history)
            if (!string.IsNullOrWhiteSpace(turn.Text)) input.Add(new { role = turn.Role == "assistant" ? "assistant" : "user", content = turn.Text });
        input.Add(new { role = "user", content = question });

        var body = new
        {
            model = Model,
            input,
            max_output_tokens = 1500,
            temperature = 0.2,
            reasoning = new { effort = reasoning ?? (string.IsNullOrWhiteSpace(config["Grok:AnalystReasoning"]) ? "medium" : config["Grok:AnalystReasoning"]!.Trim()) },
            tools = new object[]
            {
                new { type = "mcp", server_url = mcpUrl, server_label = "orderorange", server_description = "Live OrderOrange platform data: stores, menus, orders, sales, customers, bookings, inventory, visitors, raw read-only SQL and MongoDB.", authorization = mcpToken },
            },
        };
        try
        {
            var http = factory.CreateClient("grok-analyst");
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Grok analyst {Status}: {Body}", (int)res.StatusCode, json.Length > 800 ? json[..800] : json);
                return (null, [], $"Grok answered {(int)res.StatusCode}: {Snip(json)}");
            }
            var text = ExtractText(json);
            var calls = ExtractToolCalls(json);
            return (string.IsNullOrWhiteSpace(text) ? null : text.Trim(), calls, string.IsNullOrWhiteSpace(text) ? "The model returned no text." : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "Grok analyst unreachable");
            return (null, [], ex.Message);
        }
    }

    private static string Snip(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e)) return e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString();
        }
        catch (JsonException) { }
        return json.Length > 300 ? json[..300] : json;
    }

    /// <summary>Every MCP call item in the response (type "mcp_call" and friends): what was asked and what came back.</summary>
    internal static List<ToolCall> ExtractToolCalls(string json)
    {
        var list = new List<ToolCall>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in output.EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (!type.Contains("mcp", StringComparison.OrdinalIgnoreCase) || type.Contains("list_tools", StringComparison.OrdinalIgnoreCase)) continue;
            string S(string n) => item.TryGetProperty(n, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
            var err = S("error");
            list.Add(new ToolCall(S("server_label"), S("name"), S("arguments"), err.Length > 0 ? err : S("output"), err.Length > 0));
        }
        return list;
    }

    /// <summary>
    /// Agent mode asks the model for a JSON object; this reads it leniently (code fences,
    /// prose around it) and falls back to treating the whole text as the reply.
    /// </summary>
    internal static AssistantReplyDto ParseAgentReply(string raw, string model)
    {
        var s = raw.Trim();
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var doc = JsonDocument.Parse(s[start..(end + 1)]);
                var root = doc.RootElement;
                string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                int? Num(string name)
                {
                    if (!root.TryGetProperty(name, out var v)) return null;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var m)) return m;
                    return null;
                }
                var action = (Str("action") ?? "none").Trim().ToLowerInvariant();
                if (action is not ("none" or "search" or "menu" or "stores" or "near" or "pick" or "qty" or "remove" or "keep" or "track" or "cancel" or "cart" or "checkout" or "reorder" or "clear" or "help"))
                    action = "none";
                var scope = (Str("scope") ?? "").Trim().ToLowerInvariant();
                var url = (Str("url") ?? "").Trim();
                if (url.Length > 0 && !(url.StartsWith('/') || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))) url = "";
                var lang = (Str("lang") ?? "").Trim().ToLowerInvariant();
                if (lang.Length > 2) lang = lang[..2];
                return new AssistantReplyDto((Str("reply") ?? "").Trim(), model, action, Str("query")?.Trim(), Num("option"), Num("qty"), scope == "all" ? "all" : "store", url.Length > 0 ? url : null, lang.Length == 2 ? lang : null);
            }
            catch (JsonException) { /* not JSON after all — plain prose below */ }
        }
        return new AssistantReplyDto(s, model);
    }

    /// <summary>
    /// The Responses API answers with <c>output[]</c> items; the message item carries
    /// <c>content[]</c> parts of type <c>output_text</c>. Some servers also add a flat
    /// <c>output_text</c> convenience field — taken first when present.
    /// </summary>
    internal static string? ExtractText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("output_text", out var flat) && flat.ValueKind == JsonValueKind.String)
            return flat.GetString();

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return null;
        var sb = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var t) && t.GetString() != "message") continue;
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var pt) && pt.GetString() is not ("output_text" or "text")) continue;
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    sb.Append(text.GetString());
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
