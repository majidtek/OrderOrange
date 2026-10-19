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

    public App()
    {
        L.Lang = AppSettings.Current.Language;
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
