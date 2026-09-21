namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Glyphs the Material set has no answer for. Written the way MudBlazor writes its own icons —
/// bare SVG children, no wrapper — because MudIcon supplies the &lt;svg viewBox="0 0 24 24"&gt;
/// around whatever it is given.
/// </summary>
public static class BrandIcons
{
    /// <summary>The camera outline everyone reads as Instagram: rounded square, lens, flash dot.</summary>
    public const string Instagram =
        "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.9\" stroke-linecap=\"round\" stroke-linejoin=\"round\">" +
        "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"5.2\"/>" +
        "<circle cx=\"12\" cy=\"12\" r=\"4.1\"/>" +
        "</g>" +
        "<circle cx=\"17.4\" cy=\"6.6\" r=\"1.15\" fill=\"currentColor\"/>";
}
