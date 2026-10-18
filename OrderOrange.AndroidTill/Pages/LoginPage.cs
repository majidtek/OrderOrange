using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Models;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>Sign in with the shop's business account. The password is typed here and only here.</summary>
public sealed class LoginPage : ContentPage
{
    private readonly Entry _url = new() { Keyboard = Keyboard.Url };
    private readonly Entry _user = new() { Keyboard = Keyboard.Email };
    private readonly Entry _pass = new() { IsPassword = true };
    private readonly Label _error = new() { TextColor = Color.FromArgb("#C62828") };

    public LoginPage()
    {
        var s = AppSettings.Current;
        Title = L.T("appName");
        FlowDirection = Ui.Flow;
        _url.Text = s.ApiBaseUrl;
        _user.Text = s.Username;

        var lang = new Picker { Title = L.T("language") };
        foreach (var (_, name) in L.Languages) lang.Items.Add(name);
        lang.SelectedIndex = Math.Max(0, Array.FindIndex(L.Languages, l => l.Code == L.Lang));
        lang.SelectedIndexChanged += (_, _) =>
        {
            if (lang.SelectedIndex < 0) return;
            var code = L.Languages[lang.SelectedIndex].Code;
            if (code == L.Lang) return;
            L.Lang = code;
            s.Language = code;
            s.Save();
            Ui.Restart(new LoginPage());
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 8,
                Children =
                {
                    Ui.Title(L.T("appName")),
                    Ui.Caption(L.T("language")), lang,
                    Ui.Caption(L.T("apiUrl")), _url,
                    Ui.Caption(L.T("username")), _user,
                    Ui.Caption(L.T("password")), _pass,
                    new BoxView { HeightRequest = 8, Color = Colors.Transparent },
                    Ui.Primary(L.T("signIn"), SignInAsync),
                    _error,
                },
            },
        };
    }

    private async Task SignInAsync()
    {
        _error.Text = L.T("signingIn");
        var err = await TillEngine.Instance.SignInAsync(_url.Text ?? "", _user.Text ?? "", _pass.Text ?? "");
        if (err is not null) { _error.Text = err; return; }
        _error.Text = "";
        Ui.Restart(new MainPage());
    }
}
