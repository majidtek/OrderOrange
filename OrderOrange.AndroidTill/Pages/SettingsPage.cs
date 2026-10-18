using Microsoft.Maui.ApplicationModel;
using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Models;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>The global knobs: server, auto-print switches, poll interval, image conversion, language.</summary>
public sealed class SettingsPage : ContentPage
{
    private readonly Entry _url = new() { Keyboard = Keyboard.Url };
    private readonly Entry _client = new() { Keyboard = Keyboard.Url };
    private readonly Switch _autoReceipt = new();
    private readonly Switch _autoKitchen = new();
    private readonly Entry _poll = new() { Keyboard = Keyboard.Numeric, WidthRequest = 80, HorizontalTextAlignment = TextAlignment.End };
    private readonly Switch _dither = new();
    private readonly Entry _threshold = new() { Keyboard = Keyboard.Numeric, WidthRequest = 80, HorizontalTextAlignment = TextAlignment.End };
    private readonly Picker _lang = new();

    public SettingsPage()
    {
        var s = AppSettings.Current;
        Title = L.T("settings");
        FlowDirection = Ui.Flow;

        _url.Text = s.ApiBaseUrl;
        _client.Text = s.ClientUrl;
        _autoReceipt.IsToggled = s.AutoPrintReceipt;
        _autoKitchen.IsToggled = s.AutoPrintKitchen;
        _poll.Text = s.PollSeconds.ToString();
        _dither.IsToggled = s.Dither;
        _threshold.Text = s.Threshold.ToString();
        foreach (var (_, name) in L.Languages) _lang.Items.Add(name);
        _lang.SelectedIndex = Math.Max(0, Array.FindIndex(L.Languages, l => l.Code == L.Lang));

        var version = new Label { TextColor = Colors.Gray, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) };
        try { version.Text = $"{L.T("version")} {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})"; } catch { }

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 4,
                Children =
                {
                    Ui.Caption(L.T("apiUrl")), _url,
                    Ui.Caption(L.T("clientUrl")), _client,
                    Ui.Row(L.T("autoReceipt"), _autoReceipt),
                    Ui.Row(L.T("autoKitchen"), _autoKitchen),
                    Ui.Row(L.T("pollSeconds"), _poll),
                    Ui.Row(L.T("dither"), _dither),
                    Ui.Row(L.T("threshold"), _threshold),
                    Ui.Caption(L.T("language")), _lang,
                    new BoxView { HeightRequest = 8, Color = Colors.Transparent },
                    Ui.Primary(L.T("save"), SaveAsync),
                    Ui.Secondary(L.T("deviceProbe"), () => Navigation.PushAsync(new ProbePage())),
                    Ui.Secondary(L.T("viewLog"), () => Navigation.PushAsync(new LogPage())),
                    version,
                },
            },
        };
    }

    private async Task SaveAsync()
    {
        var s = AppSettings.Current;
        var url = (_url.Text ?? "").Trim();
        if (url.Length > 0 && !url.EndsWith('/')) url += "/";
        if (url.Length > 0) { s.ApiBaseUrl = url; TillEngine.Instance.Api.BaseUrl = url; }
        if (!string.IsNullOrWhiteSpace(_client.Text)) s.ClientUrl = _client.Text.Trim();
        s.AutoPrintReceipt = _autoReceipt.IsToggled;
        s.AutoPrintKitchen = _autoKitchen.IsToggled;
        if (int.TryParse(_poll.Text, out var poll)) s.PollSeconds = Math.Clamp(poll, 3, 300);
        s.Dither = _dither.IsToggled;
        if (int.TryParse(_threshold.Text, out var th)) s.Threshold = Math.Clamp(th, 1, 254);

        var code = L.Languages[Math.Max(0, _lang.SelectedIndex)].Code;
        var languageChanged = code != s.Language;
        s.Language = code;
        s.Save();

        if (languageChanged)
        {
            L.Lang = code;
            Ui.Restart(new MainPage());
            return;
        }
        await Navigation.PopAsync();
    }
}

/// <summary>What this terminal is and how its printer might be reached — to send to Majed for a new model.</summary>
public sealed class ProbePage : ContentPage
{
    private readonly Label _report = new() { FontFamily = "monospace", FontSize = 12 };

    public ProbePage()
    {
        Title = L.T("deviceProbe");
        FlowDirection = FlowDirection.LeftToRight;   // paths and ids read better left to right

        Content = new Grid
        {
            Padding = 16,
            RowSpacing = 8,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        Ui.Primary(L.T("probe"), ProbeAsync),
                        Ui.Secondary(L.T("tryBuiltIn"), TryBuiltInAsync),
                    },
                },
                new ScrollView { Content = _report },
            },
        };
        Grid.SetRow((View)((Grid)Content).Children[1], 1);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (string.IsNullOrEmpty(_report.Text)) _ = ProbeAsync();
    }

    private async Task ProbeAsync()
    {
        _report.Text = L.T("probing");
        _report.Text = await Printing.DeviceProbe.RunAsync();
    }

    private async Task TryBuiltInAsync()
    {
        var known = AppSettings.Current.Printers.FirstOrDefault(p => p.Connection == PrinterConnection.BuiltIn);
        var cfg = new PrinterConfig
        {
            Name = L.T("builtIn"), Connection = PrinterConnection.BuiltIn,
            Paper = known?.Paper ?? 58, Cut = known?.Cut ?? false, Copies = 1,
        };
        var err = await TillEngine.Instance.TestPrintAsync(cfg);
        _report.Text = (err is null
            ? $"OK via {AppSettings.Current.BuiltInRoute}"
            : $"FAILED: {err}") + "\n\n" + _report.Text;
    }
}

/// <summary>The till's diary.</summary>
public sealed class LogPage : ContentPage
{
    public LogPage()
    {
        Title = L.T("log");
        FlowDirection = FlowDirection.LeftToRight;
        Content = new ScrollView
        {
            Padding = 12,
            Content = new Label { FontFamily = "monospace", FontSize = 11, Text = TillLog.Tail() },
        };
    }
}
