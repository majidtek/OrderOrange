using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using OrderOrange.ApiServer.Data;

namespace OrderOrange.ApiServer.Mcp;

/// <summary>
/// The Model Context Protocol server, served by the API itself over Streamable HTTP at
/// <c>/mcp</c> (JSON-RPC 2.0), plus a plain REST mirror of the same tools under
/// <c>/api/mcp/*</c> with an OpenAPI description — so Grok, Claude, any MCP client and any
/// ordinary HTTP client all speak to the same catalogue.
///
/// Authentication: <c>Authorization: Bearer &lt;Mcp:Token&gt;</c>, or an OrderOrange
/// administrator's JWT. No token configured = the endpoints answer 503.
/// </summary>
public static class McpEndpoint
{
    public const string ProtocolVersion = "2025-06-18";
    private static readonly string[] SupportedVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];
    public const string ServerVersion = "1.0.0";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        // Arabic and Persian text and plain quotes stay readable in tool output instead of \u-escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IEndpointRouteBuilder MapMcp(this IEndpointRouteBuilder app)
    {
        // ── MCP Streamable HTTP ──
        app.MapPost("/mcp", HandleJsonRpc);
        app.MapGet("/mcp", (HttpContext ctx) =>
        {
            // No server-initiated stream: the spec lets a server decline with 405.
            ctx.Response.Headers.Allow = "POST, DELETE";
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        });
        app.MapDelete("/mcp", (HttpContext ctx) => Authorized(ctx) ? Results.Ok() : Results.Unauthorized());

        // ── REST mirror ("very modern API"): same tools, plain JSON ──
        app.MapGet("/api/mcp", (HttpContext ctx) => Results.Json(new
        {
            name = "OrderOrange MCP",
            version = ServerVersion,
            protocolVersion = ProtocolVersion,
            mcpEndpoint = string.IsNullOrWhiteSpace(ctx.RequestServices.GetRequiredService<IConfiguration>()["Mcp:PublicUrl"]) ? Base(ctx) + "/mcp" : ctx.RequestServices.GetRequiredService<IConfiguration>()["Mcp:PublicUrl"]!.Trim(),
            openapi = Base(ctx) + "/api/mcp/openapi.json",
            tools = Base(ctx) + "/api/mcp/tools",
            authentication = "Authorization: Bearer <token> (Mcp:Token or an administrator's JWT)",
            toolsCount = McpToolbox.All.Count,
            writesEnabled = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Mcp:AllowWrites", false),
        }, Json));
        app.MapGet("/api/mcp/tools", (HttpContext ctx) => Authorized(ctx)
            ? Results.Json(new { tools = McpToolbox.All.Select(ToolDescriptor) }, Json)
            : Results.Unauthorized());
        app.MapPost("/api/mcp/tools/{name}", async (string name, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Authorized(ctx)) return Results.Unauthorized();
            var tool = McpToolbox.Find(name);
            if (tool is null) return Results.NotFound(new { error = $"Unknown tool '{name}'." });
            JsonElement args;
            try
            {
                using var doc = ctx.Request.ContentLength is > 0 or null ? await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct) : JsonDocument.Parse("{}");
                args = doc.RootElement.Clone();
            }
            catch (JsonException) { args = JsonDocument.Parse("{}").RootElement; }
            var (result, isError) = await RunToolAsync(tool, args, ctx, ct);
            return Results.Json(result, Json, statusCode: isError ? 400 : 200);
        });
        app.MapGet("/api/mcp/openapi.json", (HttpContext ctx) => Results.Json(OpenApi(ctx), Json));
        return app;
    }

    // ───────────────────────────── auth ─────────────────────────────

    private static bool Authorized(HttpContext ctx) => ScopeOf(ctx) is not null;

    /// <summary>
    /// The caller's scope, or null when the bearer is not ours. Three kinds of bearer: the
    /// server token (whole platform, writes per config), a store-scoped token minted by
    /// <see cref="MintStoreToken"/> (one store, writes per token), or an administrator's JWT
    /// (whole platform, read-only).
    /// </summary>
    private static McpScope? ScopeOf(HttpContext ctx)
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var token = (config["Mcp:Token"] ?? "").Trim();
        if (token.Length == 0) return null;
        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var presented = header[7..].Trim();
            if (presented.Length == token.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(token)))
                return new McpScope(null, config.GetValue("Mcp:AllowWrites", false));
            if (presented.StartsWith("oo_scope.", StringComparison.Ordinal) && ReadStoreToken(presented, token) is { } scoped)
                return scoped;
        }
        // An administrator signed in to the admin app may use their own JWT.
        return ctx.User.Identity?.IsAuthenticated == true && ctx.User.IsInRole("Administrator")
            ? new McpScope(null, false)
            : null;
    }

    /// <summary>
    /// A short-lived bearer that lets a language model act for ONE store: "oo_scope.&lt;payload&gt;.&lt;hmac&gt;"
    /// where payload = base64url {"s":storeId,"w":writes,"e":unixExpiry} signed with the server token.
    /// Minted per partner-assistant question, so the model never holds the platform key.
    /// </summary>
    public static string MintStoreToken(IConfiguration config, int storeId, bool allowWrites, TimeSpan life)
    {
        var secret = (config["Mcp:Token"] ?? "").Trim();
        if (secret.Length == 0) throw new InvalidOperationException("Mcp:Token is not configured.");
        var payload = JsonSerializer.Serialize(new { s = storeId, w = allowWrites, e = DateTimeOffset.UtcNow.Add(life).ToUnixTimeSeconds() });
        var p = B64(Encoding.UTF8.GetBytes(payload));
        var sig = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(p)));
        return $"oo_scope.{p}.{sig}";
    }

    private static McpScope? ReadStoreToken(string presented, string secret)
    {
        var parts = presented.Split('.');
        if (parts.Length != 3) return null;
        var expected = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(parts[1])));
        if (expected.Length != parts[2].Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2]))) return null;
        try
        {
            using var doc = JsonDocument.Parse(UnB64(parts[1]));
            var r = doc.RootElement;
            var exp = r.GetProperty("e").GetInt64();
            if (DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow) return null;
            var storeId = r.GetProperty("s").GetInt32();
            var writes = r.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.True;
            return storeId > 0 ? new McpScope(storeId, writes) : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] UnB64(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }

    /// <summary>Tools a one-store caller must not see: the raw database and platform-wide views.</summary>
    private static readonly HashSet<string> PlatformOnlyTools = new(StringComparer.OrdinalIgnoreCase)
        { "sql_query", "mongo_find", "mongo_aggregate", "schema", "platform_overview", "list_stores", "visitors" };

    /// <summary>Pins every store-addressed argument of a scoped call to the caller's own store.</summary>
    private static JsonElement PinToStore(JsonElement args, string toolName, int storeId)
    {
        var node = args.ValueKind == JsonValueKind.Object ? JsonNode.Parse(args.GetRawText()) as JsonObject ?? new JsonObject() : new JsonObject();
        node["storeId"] = storeId;
        if (toolName.Equals("get_store", StringComparison.OrdinalIgnoreCase)) { node["id"] = storeId; node.Remove("slug"); }
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static string Base(HttpContext ctx)
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var pub = config["Api:PublicBase"] ?? config["Media:PublicBase"];
        return string.IsNullOrWhiteSpace(pub) ? $"{ctx.Request.Scheme}://{ctx.Request.Host}" : pub.TrimEnd('/');
    }

    // ───────────────────────────── JSON-RPC ─────────────────────────────

    private static async Task HandleJsonRpc(HttpContext ctx, CancellationToken ct)
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        if (string.IsNullOrWhiteSpace(config["Mcp:Token"]))
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsJsonAsync(new { error = "MCP is not configured on this server (Mcp:Token is empty)." }, ct);
            return;
        }
        if (!Authorized(ctx))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Bearer realm=\"OrderOrange MCP\"";
            await ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized: send 'Authorization: Bearer <token>'." }, ct);
            return;
        }

        JsonNode? body;
        try { body = await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ct); }
        catch (JsonException ex)
        {
            await WriteAsync(ctx, RpcError(null, -32700, "Parse error: " + ex.Message), ct);
            return;
        }

        // A batch (older protocol revisions) is answered as a batch; notifications get no reply.
        if (body is JsonArray batch)
        {
            var replies = new JsonArray();
            foreach (var item in batch)
                if (item is JsonObject o && await DispatchAsync(o, ctx, ct) is { } reply) replies.Add(reply);
            if (replies.Count == 0) { ctx.Response.StatusCode = StatusCodes.Status202Accepted; return; }
            await WriteAsync(ctx, replies, ct);
            return;
        }
        if (body is not JsonObject req)
        {
            await WriteAsync(ctx, RpcError(null, -32600, "Invalid request"), ct);
            return;
        }
        var result = await DispatchAsync(req, ctx, ct);
        if (result is null) { ctx.Response.StatusCode = StatusCodes.Status202Accepted; return; }
        await WriteAsync(ctx, result, ct);
    }

    private static async Task WriteAsync(HttpContext ctx, JsonNode node, CancellationToken ct)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(node.ToJsonString(Json), ct);
    }

    private static JsonObject RpcError(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var err = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null) err["data"] = data;
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = err };
    }

    private static JsonObject RpcResult(JsonNode? id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };

    /// <summary>One JSON-RPC message. Null = a notification, which gets no reply.</summary>
    private static async Task<JsonObject?> DispatchAsync(JsonObject req, HttpContext ctx, CancellationToken ct)
    {
        var id = req["id"];
        var method = req["method"]?.GetValue<string>() ?? "";
        var @params = req["params"] as JsonObject;
        var isNotification = id is null;

        try
        {
            switch (method)
            {
                case "initialize":
                {
                    var asked = @params?["protocolVersion"]?.GetValue<string>();
                    var version = asked is not null && SupportedVersions.Contains(asked) ? asked : ProtocolVersion;
                    ctx.Response.Headers["Mcp-Session-Id"] = Guid.NewGuid().ToString("N");
                    var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
                    return RpcResult(id, new JsonObject
                    {
                        ["protocolVersion"] = version,
                        ["capabilities"] = new JsonObject
                        {
                            ["tools"] = new JsonObject { ["listChanged"] = false },
                            ["resources"] = new JsonObject { ["subscribe"] = false, ["listChanged"] = false },
                            ["prompts"] = new JsonObject { ["listChanged"] = false },
                        },
                        ["serverInfo"] = new JsonObject { ["name"] = "orderorange", ["title"] = "OrderOrange", ["version"] = ServerVersion },
                        ["instructions"] = (ScopeOf(ctx) is { IsStore: true } sc ? $"This caller is scoped to store {sc.StoreId}: every tool answers for that store only. " : "") +
                            "OrderOrange is a food-ordering and shop platform in Oman (restaurants, cafes, shops; delivery, pickup, table QR ordering, bookings; prices in OMR). " +
                            "Start with platform_overview or list_stores; use get_menu / sales_report / top_items / customers / reservations / inventory for a store; " +
                            "call schema before sql_query (read-only T-SQL) or mongo_find / mongo_aggregate (read-only). Store ids ≥ " + config.GetValue("Seo:RealStoreMinId", 5_000_200) + " are real stores. " +
                            (config.GetValue("Mcp:AllowWrites", false) ? "Writes (set_store_open, set_item_availability) are enabled." : "This server is read-only."),
                    });
                }
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return null;
                case "ping":
                    return RpcResult(id, new JsonObject());
                case "tools/list":
                {
                    var scope = ScopeOf(ctx);
                    var visible = McpToolbox.All.Where(t => scope is not { IsStore: true } || !PlatformOnlyTools.Contains(t.Name))
                        .Where(t => t.ReadOnly || (scope?.AllowWrites ?? false));
                    return RpcResult(id, new JsonObject { ["tools"] = new JsonArray(visible.Select(t => JsonSerializer.SerializeToNode(ToolDescriptor(t), Json)!).ToArray()) });
                }
                case "tools/call":
                {
                    var name = @params?["name"]?.GetValue<string>() ?? "";
                    var tool = McpToolbox.Find(name);
                    if (tool is null) return RpcError(id, -32602, $"Unknown tool '{name}'.");
                    var args = (@params?["arguments"] as JsonObject)?.Deserialize<JsonElement>() ?? JsonDocument.Parse("{}").RootElement;
                    var (result, isError) = await RunToolAsync(tool, args, ctx, ct);
                    var node = JsonSerializer.SerializeToNode(result, Json) ?? new JsonObject();
                    var text = node.ToJsonString(Json);
                    var res = new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                        ["isError"] = isError,
                    };
                    if (node is JsonObject obj && !isError) res["structuredContent"] = obj.DeepClone();
                    return RpcResult(id, res);
                }
                case "resources/list":
                    return RpcResult(id, new JsonObject
                    {
                        ["resources"] = new JsonArray(
                            new JsonObject { ["uri"] = "orderorange://schema", ["name"] = "schema", ["title"] = "Database schema", ["description"] = "SQL tables and Mongo collections with fields and counts", ["mimeType"] = "application/json" },
                            new JsonObject { ["uri"] = "orderorange://stores", ["name"] = "stores", ["title"] = "Real stores", ["description"] = "Every hand-onboarded store with type, area and link", ["mimeType"] = "application/json" },
                            new JsonObject { ["uri"] = "orderorange://overview", ["name"] = "overview", ["title"] = "Platform overview", ["description"] = "Stores, people, orders and revenue right now", ["mimeType"] = "application/json" }),
                    });
                case "resources/read":
                {
                    var uri = @params?["uri"]?.GetValue<string>() ?? "";
                    var toolName = uri switch { "orderorange://schema" => "schema", "orderorange://stores" => "list_stores", "orderorange://overview" => "platform_overview", _ => null };
                    if (toolName is null) return RpcError(id, -32002, $"Unknown resource '{uri}'.");
                    var (result, _) = await RunToolAsync(McpToolbox.Find(toolName)!, JsonDocument.Parse("{}").RootElement, ctx, ct);
                    var text = JsonSerializer.Serialize(result, Json);
                    return RpcResult(id, new JsonObject { ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "application/json", ["text"] = text }) });
                }
                case "prompts/list":
                    return RpcResult(id, new JsonObject
                    {
                        ["prompts"] = new JsonArray(
                            new JsonObject { ["name"] = "daily_report", ["title"] = "Daily business report", ["description"] = "Yesterday vs. the day before: orders, revenue, top items, cancellations, bookings — per store and overall.", ["arguments"] = new JsonArray(new JsonObject { ["name"] = "storeId", ["description"] = "Optional store id", ["required"] = false }) },
                            new JsonObject { ["name"] = "menu_review", ["title"] = "Menu review", ["description"] = "Find menu problems for a store: missing prices or descriptions, items never ordered, unavailable items, duplicates.", ["arguments"] = new JsonArray(new JsonObject { ["name"] = "storeId", ["description"] = "Store id", ["required"] = true }) },
                            new JsonObject { ["name"] = "customer_insights", ["title"] = "Customer insights", ["description"] = "Who the best customers are, who stopped ordering, and what to offer them.", ["arguments"] = new JsonArray(new JsonObject { ["name"] = "storeId", ["description"] = "Optional store id", ["required"] = false }) }),
                    });
                case "prompts/get":
                {
                    var name = @params?["name"]?.GetValue<string>() ?? "";
                    var storeId = (@params?["arguments"] as JsonObject)?["storeId"]?.ToString();
                    var scope = string.IsNullOrWhiteSpace(storeId) ? "all real stores" : $"store {storeId}";
                    var text = name switch
                    {
                        "daily_report" => $"Using the OrderOrange tools, build a daily business report for {scope}. Use sales_report (groupBy day) for the last 7 days and compare yesterday with the day before, top_items for yesterday, list_orders with status Cancelled for yesterday, and reservations for today. Report in short sections with numbers in OMR; end with three concrete actions.",
                        "menu_review" => $"Review the menu of {scope} with get_menu and top_items (last 90 days). Flag items with no description, items never ordered, unavailable items, near-duplicate names and price outliers within a category. Suggest fixes as a checklist.",
                        "customer_insights" => $"Using customers (last 180 days, limit 100) and sales_report for {scope}, identify the top 10 customers by spend, customers whose last order is older than 45 days, and the weekday/hour peaks. Propose two retention offers with expected impact.",
                        _ => null,
                    };
                    if (text is null) return RpcError(id, -32602, $"Unknown prompt '{name}'.");
                    return RpcResult(id, new JsonObject { ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonObject { ["type"] = "text", ["text"] = text } }) });
                }
                default:
                    return isNotification ? null : RpcError(id, -32601, $"Method not found: {method}");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mcp").LogError(ex, "MCP {Method} failed", method);
            return isNotification ? null : RpcError(id, -32603, "Internal error: " + ex.Message);
        }
    }

    // ───────────────────────────── tools ─────────────────────────────

    private static object ToolDescriptor(McpTool t) => new
    {
        name = t.Name,
        title = t.Title,
        description = t.Description,
        inputSchema = t.InputSchema,
        annotations = new { title = t.Title, readOnlyHint = t.ReadOnly, destructiveHint = !t.ReadOnly, idempotentHint = true, openWorldHint = false },
    };

    private static async Task<(object Result, bool IsError)> RunToolAsync(McpTool tool, JsonElement args, HttpContext ctx, CancellationToken ct)
    {
        var sp = ctx.RequestServices;
        var scope = ScopeOf(ctx) ?? new McpScope(null, false);
        if (scope.IsStore)
        {
            if (PlatformOnlyTools.Contains(tool.Name)) return (McpToolbox.Error($"'{tool.Name}' is not available to a store-scoped caller; use the store tools (get_store, get_menu, list_orders, sales_report, top_items, customers, reservations, inventory, staff, assistant_chats)."), true);
            args = PinToStore(args, tool.Name, scope.StoreId!.Value);
        }
        var context = new ToolContext(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IMongoDatabase>(), sp.GetRequiredService<CatalogStore>(), sp.GetRequiredService<IConfiguration>(), scope);
        if (!tool.ReadOnly && !context.WritesAllowed) return (McpToolbox.Error("Writes are not allowed for this caller."), true);
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Mcp");
        var started = DateTime.UtcNow;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var result = await tool.Run(context, new Args(args), timeout.Token) ?? new { };
            var isError = result is McpToolbox.ToolError;
            log.LogInformation("MCP tool {Tool} {Outcome} in {Ms} ms", tool.Name, isError ? "refused" : "ok", (int)(DateTime.UtcNow - started).TotalMilliseconds);
            return (result, isError);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (McpToolbox.Error("The tool took longer than 60 seconds and was stopped."), true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "MCP tool {Tool} failed", tool.Name);
            return (McpToolbox.Error(ex.Message), true);
        }
    }

    // ───────────────────────────── OpenAPI for the REST mirror ─────────────────────────────

    private static object OpenApi(HttpContext ctx)
    {
        var paths = new Dictionary<string, object>
        {
            ["/api/mcp/tools"] = new { get = new { summary = "List the tools", operationId = "listTools", security = new[] { new Dictionary<string, string[]> { ["bearer"] = [] } }, responses = new Dictionary<string, object> { ["200"] = new { description = "Tool descriptors with JSON Schema inputs" } } } },
        };
        foreach (var t in McpToolbox.All)
        {
            paths[$"/api/mcp/tools/{t.Name}"] = new
            {
                post = new
                {
                    summary = t.Title,
                    description = t.Description,
                    operationId = t.Name,
                    tags = new[] { t.ReadOnly ? "read" : "write" },
                    security = new[] { new Dictionary<string, string[]> { ["bearer"] = [] } },
                    requestBody = new { required = false, content = new Dictionary<string, object> { ["application/json"] = new { schema = t.InputSchema } } },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new { description = "Result", content = new Dictionary<string, object> { ["application/json"] = new { schema = new { type = "object" } } } },
                        ["400"] = new { description = "Refused or failed", content = new Dictionary<string, object> { ["application/json"] = new { schema = new { type = "object", properties = new { error = new { type = "string" } } } } } },
                        ["401"] = new { description = "Missing or wrong bearer token" },
                    },
                },
            };
        }
        return new
        {
            openapi = "3.1.0",
            info = new { title = "OrderOrange MCP tools", version = ServerVersion, description = "The same tools the MCP server exposes at /mcp, callable as plain REST. Read-only unless the server enables writes." },
            servers = new[] { new { url = Base(ctx) } },
            components = new { securitySchemes = new { bearer = new { type = "http", scheme = "bearer" } } },
            paths,
        };
    }
}
