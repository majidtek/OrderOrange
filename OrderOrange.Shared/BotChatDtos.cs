namespace OrderOrange.Shared;

/// <summary>What the browser sends after one exchange with an assistant.</summary>
/// <param name="Session">A random id the page keeps for the sitting, so turns group into a conversation.</param>
/// <param name="Bot">"customer", "partner" or "pos".</param>
public record BotChatLogRequest(
    string Session,
    string Bot,
    string Text,
    string Reply,
    string? Page = null,
    string? Lang = null,
    int? StoreId = null,
    /// <summary>The address the server-rendered app saw; believed only from our own box.</summary>
    string? Ip = null);

/// <summary>One address on the report's front page.</summary>
public record BotChatIpDto(
    string Ip,
    int Turns,
    int Sessions,
    DateTime First,
    DateTime Last,
    string LastText,
    string? Country);

public record BotChatIpsDto(List<BotChatIpDto> Items, int Total);

/// <summary>One thing somebody typed, and what came back.</summary>
public record BotChatTurnDto(
    int Id,
    string Session,
    string Bot,
    string Text,
    string Reply,
    string? Page,
    string? Lang,
    int? StoreId,
    string? UserName,
    DateTime At);

public record BotChatTurnsDto(string Ip, List<BotChatTurnDto> Items);
