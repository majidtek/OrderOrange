using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Models;
using OrderOrange.AndroidTill.Pages;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill;

/// <summary>
/// The Android till. One window, one navigation stack: sign in → main board, with the
/// printers and settings pages pushed on top. No XAML — the pages are plain C#.
/// </summary>
public sealed class App : Application
{
    public static readonly Color Orange = Color.FromArgb("#F26B1D");
    // The till's palette (Majed 2026-10-22: "main menu ... have not style, it just black"):
    // warm paper ground, white cards, dark ink, the brand orange for what is tapped.
    public static readonly Color Paper = Color.FromArgb("#FAF7F2");
    public static readonly Color Card = Colors.White;
    public static readonly Color Ink = Color.FromArgb("#2B2118");
    public static readonly Color Muted = Color.FromArgb("#8A7A68");
    public static readonly Color Line = Color.FromArgb("#EFE6DA");
    public static readonly Color OrangeSoft = Color.FromArgb("#FFF1E8");
    public static readonly Color Good = Color.FromArgb("#2E7D32");
    public static readonly Color Bad = Color.FromArgb("#C62828");

    public App()
    {
        L.Lang = AppSettings.Current.Language;
        // A terminal or phone in dark mode painted every page black with default controls;
        // the till has one designed look, so it never follows the system theme.
        UserAppTheme = AppTheme.Light;
        AddStyles();
    }

    /// <summary>The look every page gets without asking: paper ground, ink text, orange controls.</summary>
    private void AddStyles()
    {
        Style For<T>(params (BindableProperty Property, object Value)[] setters) where T : BindableObject
        {
            var style = new Style(typeof(T)) { ApplyToDerivedTypes = true };
            foreach (var (property, value) in setters) style.Setters.Add(new Setter { Property = property, Value = value });
            return style;
        }

        Resources.Add(For<ContentPage>((VisualElement.BackgroundColorProperty, Paper)));
        Resources.Add(For<Label>((Label.TextColorProperty, Ink)));
        Resources.Add(For<Entry>((Entry.TextColorProperty, Ink), (Entry.PlaceholderColorProperty, Muted),
            (VisualElement.BackgroundColorProperty, Card)));
        Resources.Add(For<Editor>((Editor.TextColorProperty, Ink), (VisualElement.BackgroundColorProperty, Card)));
        Resources.Add(For<Picker>((Picker.TextColorProperty, Ink), (Picker.TitleColorProperty, Muted),
            (VisualElement.BackgroundColorProperty, Card)));
        Resources.Add(For<Switch>((Switch.OnColorProperty, Orange), (Switch.ThumbColorProperty, Colors.White)));
        Resources.Add(For<CheckBox>((CheckBox.ColorProperty, Orange)));
        Resources.Add(For<ActivityIndicator>((ActivityIndicator.ColorProperty, Orange)));
        Resources.Add(For<ProgressBar>((ProgressBar.ProgressColorProperty, Orange)));
        Resources.Add(For<Slider>((Slider.MinimumTrackColorProperty, Orange), (Slider.ThumbColorProperty, Orange)));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Page start = TillEngine.Instance.HasSavedSession ? new MainPage() : new LoginPage();
        return new Window(new NavigationPage(start)
        {
            BarBackgroundColor = Orange,
            BarTextColor = Colors.White,
        });
    }
}
