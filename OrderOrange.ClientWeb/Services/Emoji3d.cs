namespace OrderOrange.ClientWeb.Services;

/// <summary>
/// Maps a system emoji to its Microsoft Fluent 3D artwork under img/emoji3d.
///
/// This used to be done by a script that emptied the element and appended an img.
/// That mutates DOM Blazor owns, so the next render diff targeted nodes the script
/// had already replaced and the circuit died with "error applying batch". Deciding
/// the artwork here keeps every node under Blazor's control.
/// </summary>
public static class Emoji3d
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["🍕"] = "pizza", ["🍔"] = "hamburger", ["🌯"] = "burrito", ["🌮"] = "taco", ["🍣"] = "sushi",
        ["🍟"] = "french-fries", ["🍗"] = "poultry-leg", ["🍖"] = "meat-on-bone", ["🥗"] = "green-salad",
        ["🍰"] = "shortcake", ["🎂"] = "birthday-cake", ["🍩"] = "doughnut", ["🍪"] = "cookie",
        ["☕"] = "hot-beverage", ["🥤"] = "cup-with-straw", ["🧋"] = "bubble-tea", ["🍚"] = "cooked-rice",
        ["🍛"] = "curry-rice", ["🍜"] = "steaming-bowl", ["🍝"] = "spaghetti", ["🥘"] = "shallow-pan-of-food",
        ["🍲"] = "pot-of-food", ["🥙"] = "stuffed-flatbread", ["🧆"] = "falafel", ["🥪"] = "sandwich",
        ["🌭"] = "hot-dog", ["🍦"] = "soft-ice-cream", ["🍨"] = "ice-cream", ["🥞"] = "pancakes", ["🧇"] = "waffle",
        ["🍎"] = "red-apple", ["🍇"] = "grapes", ["🥭"] = "mango", ["🍉"] = "watermelon", ["🍌"] = "banana",
        ["🍹"] = "tropical-drink", ["🧃"] = "beverage-box", ["💐"] = "bouquet", ["🌹"] = "rose",
        ["🌸"] = "cherry-blossom", ["🌷"] = "tulip", ["🌻"] = "sunflower", ["💊"] = "pill",
        ["🛒"] = "shopping-cart", ["🛍"] = "shopping-bags", ["🏪"] = "convenience-store",
        ["🍽"] = "fork-and-knife-with-plate", ["❤"] = "red-heart", ["🍳"] = "cooking", ["🥩"] = "cut-of-meat",
        ["🍤"] = "fried-shrimp", ["🥐"] = "croissant", ["🍞"] = "bread", ["🧁"] = "cupcake",
        ["🍫"] = "chocolate-bar", ["🍿"] = "popcorn", ["🥟"] = "dumpling", ["🍱"] = "bento-box",
        ["🍧"] = "shaved-ice", ["🥣"] = "bowl-with-spoon", ["🫖"] = "teapot", ["🍵"] = "teacup-without-handle",
        ["🥑"] = "avocado", ["🍊"] = "tangerine", ["🍓"] = "strawberry", ["🥦"] = "broccoli",
        ["🧀"] = "cheese-wedge", ["🥚"] = "egg", ["🍯"] = "honey-pot", ["🥜"] = "peanuts",
        ["🦐"] = "shrimp", ["🦞"] = "lobster", ["🐟"] = "fish",
    };

    /// <summary>The artwork for an emoji, or null when there is none and the glyph should show.</summary>
    public static string? PathFor(string? emoji)
    {
        var key = Strip(emoji);
        return key is not null && Map.TryGetValue(key, out var slug) ? $"/img/emoji3d/{slug}.png" : null;
    }

    /// <summary>Drops variation selectors and zero-width joiners so "❤️" matches "❤".</summary>
    private static string? Strip(string? emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return null;
        Span<char> buffer = stackalloc char[emoji.Length];
        var n = 0;
        foreach (var ch in emoji.Trim())
            if (ch is not ((>= '︀' and <= '️') or '‍'))
                buffer[n++] = ch;
        return n == 0 ? null : new string(buffer[..n]);
    }
}
