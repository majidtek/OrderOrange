namespace OrderOrange.ClientCore.Services;

/// <summary>Real-photo profile avatars shipped in ClientCore (wwwroot/avatars).</summary>
public static class Avatars
{
    /// <summary>The preset portrait files users can pick from.</summary>
    public static readonly string[] Choices =
        ["a1.jpg", "a2.jpg", "a3.jpg", "a4.jpg", "a5.jpg", "a6.jpg",
         "a7.jpg", "a8.jpg", "a9.jpg", "a10.jpg", "a11.jpg", "a12.jpg"];

    /// <summary>Absolute asset URL for a stored avatar value, or null when the
    /// value is not a photo (legacy emoji or unset → caller shows a fallback).</summary>
    public static string? Url(string? icon) =>
        icon is not null && icon.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            ? $"_content/OrderOrange.ClientCore/avatars/{icon}"
            : null;
}
