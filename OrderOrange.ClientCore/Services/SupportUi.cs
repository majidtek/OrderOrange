namespace OrderOrange.ClientCore.Services;

/// <summary>The support ticket vocabulary, as colours and glyphs — shared by both apps.</summary>
public static class SupportUi
{
    public static string StatusCss(string status) => status switch
    {
        "open" => "open", "in_progress" => "prog", "waiting" => "wait",
        "resolved" => "done", "closed" => "closed", _ => "open",
    };

    public static string CategoryEmoji(string category) => category switch
    {
        "technical" => "🛠️", "orders" => "🧾", "payments" => "💳", "menu" => "🍽️",
        "account" => "👥", "feature" => "💡", _ => "❓",
    };

    public static string PriorityColor(string priority) => priority switch
    {
        "low" => "#94a3b8", "high" => "#f59e0b", "urgent" => "#ef4444", _ => "#0ea5e9",
    };

    /// <summary>0 opened · 1 being handled · 2 resolved · 3 closed — the timeline strip.</summary>
    public static int TimelineStep(string status) => status switch
    {
        "in_progress" or "waiting" => 1, "resolved" => 2, "closed" => 3, _ => 0,
    };

    public static string HumanSize(int bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.#} MB";
}
