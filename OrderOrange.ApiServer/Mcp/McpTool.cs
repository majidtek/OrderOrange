using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using OrderOrange.ApiServer.Data;

namespace OrderOrange.ApiServer.Mcp;

/// <summary>
/// Who is calling and what they may see. The server's own token sees everything; a store's
/// scoped token (minted for the partner assistant) sees one store only and may write to it
/// when the token says so.
/// </summary>
public sealed record McpScope(int? StoreId, bool AllowWrites)
{
    public bool IsStore => StoreId is > 0;
}

/// <summary>Everything a tool may touch — one request's scoped services, and the caller's scope.</summary>
public sealed record ToolContext(AppDbContext Db, IMongoDatabase Mongo, CatalogStore Catalog, IConfiguration Config, McpScope Scope)
{
    /// <summary>Hand-onboarded stores start here; below it is the generated demo catalogue.</summary>
    public int RealStoreFloor => Config.GetValue("Seo:RealStoreMinId", 5_000_200);
    public bool WritesAllowed => Scope.AllowWrites;
    /// <summary>The one store a scoped caller may touch; null for the platform token.</summary>
    public int? ScopeStoreId => Scope.IsStore ? Scope.StoreId : null;
}

/// <summary>
/// One MCP tool: its contract (name, description, JSON Schema for the arguments) and the
/// code that runs it. <see cref="ReadOnly"/> is surfaced to clients as the MCP
/// <c>readOnlyHint</c> annotation and gates the whole tool when writes are disabled.
/// </summary>
public sealed record McpTool(
    string Name,
    string Title,
    string Description,
    string InputSchemaJson,
    bool ReadOnly,
    Func<ToolContext, Args, CancellationToken, Task<object?>> Run)
{
    public JsonElement InputSchema => JsonDocument.Parse(InputSchemaJson).RootElement.Clone();
}

/// <summary>Typed, forgiving access to a tool call's arguments.</summary>
public sealed class Args(JsonElement root)
{
    public JsonElement Root => root;

    private bool TryGet(string name, out JsonElement v)
    {
        v = default;
        if (root.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in root.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { v = p.Value; return v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined); }
        return false;
    }

    public string? Str(string name) => TryGet(name, out var v)
        ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
        : null;

    public int Int(string name, int fallback) => TryGet(name, out var v)
        ? v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n
          : int.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var m) ? m : fallback
        : fallback;

    public int? IntOrNull(string name) => TryGet(name, out _) ? Int(name, 0) : null;

    public decimal? Dec(string name) => TryGet(name, out var v)
        ? v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d
          : decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var e) ? e : null
        : null;

    public bool Bool(string name, bool fallback) => TryGet(name, out var v)
        ? v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b)
        : fallback;

    /// <summary>ISO date or date-time; a bare date means midnight local time.</summary>
    public DateTime? Date(string name)
    {
        var s = Str(name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
    }

    public JsonElement? Element(string name) => TryGet(name, out var v) ? v : null;

    /// <summary>A date window: defaults to the last 30 days; an exclusive end when only a date was given.</summary>
    public (DateTime From, DateTime To) Window(int defaultDays = 30)
    {
        var to = Date("to") ?? DateTime.Now;
        if (Str("to") is { Length: 10 }) to = to.Date.AddDays(1);        // "2026-10-01" means the whole day
        var from = Date("from") ?? to.AddDays(-defaultDays);
        if (from > to) (from, to) = (to, from);
        return (from, to);
    }
}
