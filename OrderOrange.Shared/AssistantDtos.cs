namespace OrderOrange.Shared;

/// <summary>One earlier line of the conversation, so the model answers in context.</summary>
/// <param name="Role">"user" or "assistant".</param>
public record AssistantTurnDto(string Role, string Text);

/// <summary>
/// One line the customer typed, handed to the language model behind
/// <c>api/assistant/reply</c>. The browser sends only what it already knows: the text, the
/// page, the store it is looking at, the last few lines of the chat, and — in agent mode —
/// where the ordering engine stands (step, basket, the list on screen) so the model can
/// steer it.
/// </summary>
/// <param name="Session">The sitting id the chat log uses, so the two reports line up.</param>
/// <param name="StoreId">The store whose page the customer is on, when there is one.</param>
/// <param name="Ip">The address the server-rendered app saw; believed only from our own box.</param>
/// <param name="Agent">True = answer with an action for the ordering engine (JSON), not just prose.</param>
/// <param name="Step">What the engine is waiting for ("idle", "pick-result", "pick-quantity", …).</param>
/// <param name="Cart">The basket as text, one line per item.</param>
/// <param name="Options">The numbered list currently on screen, if any.</param>
public record AssistantAskRequest(
    string Session,
    string Text,
    string? Lang = null,
    string? Page = null,
    int? StoreId = null,
    List<AssistantTurnDto>? History = null,
    string? Ip = null,
    bool Agent = false,
    string? Step = null,
    string? Cart = null,
    List<string>? Options = null);

/// <summary>An administrator's question for the data analyst (Grok + the OrderOrange MCP tools).</summary>
public record AnalystAskRequest(string Question, List<AssistantTurnDto>? History = null, string? Lang = null);

/// <summary>One tool the analyst used, shown so the numbers can be checked.</summary>
public record AnalystToolCallDto(string Tool, string Arguments, string Output, bool Error);

/// <summary>The analyst's answer: prose plus the tool calls behind it.</summary>
public record AnalystReplyDto(string? Answer, string Model, List<AnalystToolCallDto> ToolCalls, string? Error = null, int ElapsedMs = 0);

/// <summary>
/// What came back. <see cref="Model"/> names the engine for the admin report. In agent mode
/// <see cref="Action"/> tells the ordering engine what to do: none, search (with
/// <see cref="Query"/>), pick (<see cref="Option"/>, 1-based), qty (<see cref="Qty"/>),
/// track, cancel, cart, checkout, reorder, clear, help.
/// </summary>
public record AssistantReplyDto(
    string Reply,
    string Model,
    string Action = "none",
    string? Query = null,
    int? Option = null,
    int? Qty = null,
    /// <summary>For search: "store" (default, the store being viewed) or "all" (the whole platform).</summary>
    string? Scope = null,
    /// <summary>A page of the site the answer points to, offered as an "Open" button.</summary>
    string? Url = null,
    /// <summary>ISO 639-1 code of the language the customer wrote in, as the model read it.</summary>
    string? Lang = null);

/// <summary>Translate one short name (menu group, dish) into the given language codes.</summary>
public record TranslateNamesRequest(string Text, string? From, List<string> To);
