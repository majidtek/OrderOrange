using MudBlazor;

namespace OrderOrange.ClientCore.Theme;

/// <summary>
/// MajidFood brand theme — appetizing Talabat-style orange on warm, light surfaces,
/// with a dark navigation rail for the back-office apps.
/// </summary>
public static class FoodTheme
{
    public const string Orange = "#FF5A00";
    public const string OrangeDark = "#E04E00";
    public const string OrangeSoft = "#FFF1E8";
    public const string Ink = "#241F1B";

    private const string RailBg = "#201A16";
    private const string RailText = "#D8CFC8";

    private const string AppBg = "#FAF7F4";
    private const string Surface = "#FFFFFF";
    private const string TextPrimary = "#241F1B";
    private const string TextSecondary = "#77706A";
    private const string Border = "#EEE7E1";

    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Orange,
            PrimaryDarken = OrangeDark,
            Secondary = Ink,
            Tertiary = "#12A150",
            AppbarBackground = Surface,
            AppbarText = TextPrimary,
            DrawerBackground = RailBg,
            DrawerText = RailText,
            DrawerIcon = RailText,
            Background = AppBg,
            BackgroundGray = "#F3EEE9",
            Surface = Surface,
            TextPrimary = TextPrimary,
            TextSecondary = TextSecondary,
            TextDisabled = "#B4ADA7",
            ActionDefault = "#8A827B",
            ActionDisabled = "#CCC5BF",
            Divider = Border,
            DividerLight = "#F5F0EB",
            LinesDefault = Border,
            LinesInputs = "#DDD5CE",
            TableLines = Border,
            TableStriped = "#FBF8F5",
            TableHover = "#F6F1EC",
            GrayLight = "#F3EEE9",
            GrayLighter = "#F9F6F2",
            Success = "#12A150",
            SuccessDarken = "#0E8442",
            Warning = "#E08A0B",
            Error = "#D92D20",
            Info = "#3B6FE0",
            Dark = Ink,

            // MudBlazor's stock scrim is #212121b3 — 70% opaque, which turns the page
            // behind a dialog into a black wall. A softer warm veil keeps the shop
            // visible underneath while still pushing the dialog forward.
            OverlayDark = "rgba(38, 24, 12, 0.38)",
            OverlayLight = "rgba(255, 255, 255, 0.55)",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#FF7A33",
            Secondary = "#D8CFC8",
            AppbarBackground = "#26201B",
            AppbarText = "#FFFFFF",
            DrawerBackground = "#1A1512",
            DrawerText = RailText,
            DrawerIcon = RailText,
            Background = "#1D1814",
            Surface = "#26201B",
            TextPrimary = "#F6F2EE",
            TextSecondary = "#ABA39C",
            Divider = "#38302A",
            LinesDefault = "#38302A",
            Success = "#12A150",
            Warning = "#E08A0B",
            Error = "#F0455E"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "264px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Segoe UI Variable Text", "Segoe UI", "system-ui", "-apple-system", "Helvetica", "Arial", "sans-serif" },
                FontSize = "0.875rem",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = "normal"
            },
            H4 = new H4Typography { FontFamily = new[] { "Segoe UI Variable Display", "Segoe UI", "system-ui", "sans-serif" }, FontWeight = "700", FontSize = "1.65rem", LetterSpacing = "-0.02em" },
            H5 = new H5Typography { FontWeight = "700", FontSize = "1.3rem", LetterSpacing = "-0.015em" },
            H6 = new H6Typography { FontWeight = "600", FontSize = "1.1rem", LetterSpacing = "-0.01em" },
            Subtitle1 = new Subtitle1Typography { FontWeight = "600" },
            Subtitle2 = new Subtitle2Typography { FontWeight = "600" },
            Button = new ButtonTypography { FontWeight = "600", TextTransform = "none", LetterSpacing = "normal" },
            Overline = new OverlineTypography { FontWeight = "600", FontSize = "0.68rem", LetterSpacing = "0.08em", TextTransform = "uppercase" },
            Caption = new CaptionTypography { FontSize = "0.78rem" }
        }
    };
}
