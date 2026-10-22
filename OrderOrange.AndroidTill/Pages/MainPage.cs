using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>
/// The home screen: a card with the store and whether the till is running, the green update
/// strip and the red printer banner when they apply, the menu as a grid of tiles, and the
/// last 20 print jobs. The whole screen scrolls as one list, so a small phone reaches it all.
/// </summary>
public sealed class MainPage : ContentPage
{
    private static TillEngine E => TillEngine.Instance;

    private readonly Label _store = new() { FontSize = 22, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label _run = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly BoxView _runDot = new() { WidthRequest = 8, HeightRequest = 8, CornerRadius = 4, VerticalOptions = LayoutOptions.Center };
    private readonly Label _status = new() { FontSize = 13, TextColor = App.Muted, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _banner = new() { TextColor = Colors.White, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Border _bannerCard;
    private readonly Picker _stores = new() { IsVisible = false };
    private readonly CollectionView _jobs = new() { SelectionMode = SelectionMode.None };
    private readonly Label _noJobs = new() { TextColor = App.Muted, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12) };
    private bool _fillingStores;

    // Self-update: a green strip with the new version and an Update button, shown when the server has a newer APK.
    private readonly Label _updateText = new() { TextColor = Colors.White, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Button _updateBtn;
    private readonly Grid _update = new()
    {
        ColumnSpacing = 10,
        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
    };
    private readonly Border _updateCard;
    private bool _updating;

    public MainPage()
    {
        Title = L.T("appName");
        FlowDirection = Ui.Flow;

        _updateBtn = new Button { Text = L.T("updateNow"), BackgroundColor = Colors.White, TextColor = App.Good, CornerRadius = 10, Padding = new Thickness(14, 6), FontAttributes = FontAttributes.Bold };
        _updateBtn.Clicked += async (_, _) => await UpdateNowAsync();
        _update.Add(_updateText, 0, 0);
        _update.Add(_updateBtn, 1, 0);
        _updateCard = Strip(_update, App.Good);
        _bannerCard = Strip(_banner, App.Bad);

        _stores.Title = L.T("switchStore");
        _stores.SelectedIndexChanged += async (_, _) =>
        {
            if (_fillingStores || _stores.SelectedIndex < 0 || _stores.SelectedIndex >= E.Stores.Count) return;
            var store = E.Stores[_stores.SelectedIndex];
            var err = await E.SwitchStoreAsync(store.Id);
            if (err is not null) { await Ui.Alert(err); Render(); }
        };

        // One print job: a dot that says printed or failed, what it was and where it went, and when.
        _jobs.ItemTemplate = new DataTemplate(() =>
        {
            var dot = new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 5, 0, 0) };
            dot.SetBinding(BoxView.ColorProperty, "Ok", converter: new OkColorConverter());
            var title = new Label { FontAttributes = FontAttributes.Bold, FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation };
            title.SetBinding(Label.TextProperty, "Title");
            var printer = new Label { FontSize = 12, TextColor = App.Muted, LineBreakMode = LineBreakMode.TailTruncation };
            printer.SetBinding(Label.TextProperty, "Printer");
            var result = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold };
            result.SetBinding(Label.TextProperty, "Result");
            result.SetBinding(Label.TextColorProperty, "Ok", converter: new OkColorConverter());
            var time = new Label { FontSize = 12, TextColor = App.Muted, VerticalOptions = LayoutOptions.Start };
            time.SetBinding(Label.TextProperty, "Time");
            var grid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 12,
            };
            grid.Add(dot, 0, 0);
            grid.Add(new VerticalStackLayout { Spacing = 1, Children = { title, printer, result } }, 1, 0);
            grid.Add(time, 2, 0);
            // Flat rows: a list of shadows reads as noise.
            var row = Ui.Card(grid, 12, shadow: false);
            row.Margin = new Thickness(16, 0, 16, 8);
            return row;
        });

        // The store and whether the till is running.
        var who = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
        };
        who.Add(_store, 0, 0);
        who.Add(Ui.Pill(_run, _runDot), 1, 0);
        var head = Ui.Card(new VerticalStackLayout { Spacing = 6, Children = { who, _status, _stores } }, 16);

        // The menu: two columns of tiles, the everyday ones first.
        var menu = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 12, RowSpacing = 12,
        };
        menu.Add(Ui.Tile("🔄", L.T("checkNow"), App.Orange, () => E.RefreshAsync()), 0, 0);
        menu.Add(Ui.Tile("🖨️", L.T("printers"), Color.FromArgb("#0E7FD1"), () => Navigation.PushAsync(new PrintersPage())), 1, 0);
        menu.Add(Ui.Tile("🧾", L.T("reprintReceipt"), App.Good, async () => { var e = await E.ReprintLastReceiptAsync(); if (e is not null) await Ui.Alert(e); }), 0, 1);
        menu.Add(Ui.Tile("👨‍🍳", L.T("reprintKitchen"), Color.FromArgb("#D97706"), async () => { var e = await E.ReprintLastTicketsAsync(); if (e is not null) await Ui.Alert(e); }), 1, 1);
        menu.Add(Ui.Tile("🧭", L.T("routing"), Color.FromArgb("#7C3AED"), () => Navigation.PushAsync(new RoutingPage())), 0, 2);
        menu.Add(Ui.Tile("⚙️", L.T("settings"), App.Muted, () => Navigation.PushAsync(new SettingsPage())), 1, 2);

        var jobsTitle = new Label { Text = L.T("jobs"), FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(2, 10, 0, 0) };

        // Sign out sits at the very end, quiet and red: it is rare and it stops the printing.
        var signOut = new Button
        {
            Text = L.T("signOut"), TextColor = App.Bad, BackgroundColor = Colors.Transparent,
            BorderColor = App.Bad.WithAlpha(0.35f), BorderWidth = 1, CornerRadius = 12, Padding = new Thickness(12, 10),
            Margin = new Thickness(16, 8, 16, 24),
        };
        signOut.Clicked += async (_, _) => await SignOutAsync();

        // Everything above the jobs scrolls with them, so a small screen still reaches the list.
        _jobs.Header = new VerticalStackLayout
        {
            Padding = new Thickness(16, 16, 16, 8),
            Spacing = 12,
            Children = { head, _updateCard, _bannerCard, menu, jobsTitle, _noJobs },
        };
        _jobs.Footer = signOut;
        Content = _jobs;
    }

    /// <summary>A full-width rounded strip in one colour: the update offer, the printer error.</summary>
    private static Border Strip(View content, Color color) => new()
    {
        Content = content,
        BackgroundColor = color,
        StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
        Padding = new Thickness(14, 10),
        IsVisible = false,
    };

    protected override void OnAppearing()
    {
        base.OnAppearing();
        E.Changed += OnChanged;
        Render();
        _ = InitAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        E.Changed -= OnChanged;
    }

    private async Task InitAsync()
    {
        try { await Permissions.RequestAsync<Permissions.PostNotifications>(); } catch { }
        if (!E.Api.SignedIn)
        {
            bool ok;
            try { ok = await E.RestoreSessionAsync(); }
            catch { ok = false; }
            if (!ok) { Ui.Restart(new LoginPage()); return; }
        }
        MainActivity.StartTillService();
        E.Start();
        Render();
        // Is there a newer till? Ask once on open; the engine re-asks with every store refresh.
        try { if (await UpdateService.CheckAsync(E.Api) is not null) Render(); } catch { }

        // Once: ask Android not to doze the till, or orders stop arriving when the screen sleeps.
        if (!MainActivity.BackgroundAllowed() && !Preferences.Default.Get("till.batteryAsked", false))
        {
            Preferences.Default.Set("till.batteryAsked", true);
            if (await Ui.Confirm(L.T("backgroundAsk"))) MainActivity.RequestBackgroundAllowed();
        }
        // Once: "Display over other apps" — without it a ticket printed while another app is
        // in front comes out blank (Chromium does not paint in a hidden window).
        else if (!Printing.HtmlRenderer.CanUseOverlay() && !Preferences.Default.Get("till.overlayAsked", false))
        {
            Preferences.Default.Set("till.overlayAsked", true);
            if (await Ui.Confirm(L.T("overlayAsk"))) Printing.HtmlRenderer.RequestOverlay();
        }
    }

    private void OnChanged() => MainThread.BeginInvokeOnMainThread(Render);

    private void Render()
    {
        _store.Text = E.Api.StoreName ?? L.T("notSignedIn");
        _run.Text = E.Running ? L.T("running") : L.T("stopped");
        _run.TextColor = E.Running ? App.Good : App.Muted;
        _runDot.Color = E.Running ? App.Good : App.Muted;
        _status.Text = E.Status ?? "";
        _status.IsVisible = !string.IsNullOrEmpty(E.Status);
        _banner.Text = E.Banner ?? "";
        _bannerCard.IsVisible = E.Banner is not null;
        if (!_updating)
        {
            var up = UpdateService.Available;
            _updateCard.IsVisible = up is not null;
            if (up is not null) _updateText.Text = string.Format(L.T("updateAvailable"), up.Version, up.Notes ?? "");
        }

        if (E.Stores.Count > 1)
        {
            _fillingStores = true;
            var names = E.Stores.Select(s => s.Name).ToList();
            if (!_stores.Items.SequenceEqual(names))
            {
                _stores.Items.Clear();
                foreach (var n in names) _stores.Items.Add(n);
            }
            _stores.SelectedIndex = E.Stores.FindIndex(s => s.Id == E.Api.RestaurantId);
            _stores.IsVisible = true;
            _fillingStores = false;
        }
        else _stores.IsVisible = false;

        List<PrintJob> jobs;
        lock (E.Jobs) jobs = E.Jobs.ToList();
        _jobs.ItemsSource = jobs;
        _noJobs.Text = jobs.Count == 0 ? (E.Settings.Printers.Count == 0 ? L.T("noPrinters") : L.T("noJobs")) : "";
        _noJobs.IsVisible = jobs.Count == 0;
    }

    /// <summary>Downloads the offered release with a percent counter in the strip, then opens Android's installer.</summary>
    private async Task UpdateNowAsync()
    {
        if (_updating || UpdateService.Available is not { } release) return;
        _updating = true;
        _updateBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(p => MainThread.BeginInvokeOnMainThread(() => _updateText.Text = string.Format(L.T("updateDownloading"), p)));
            _updateText.Text = string.Format(L.T("updateDownloading"), 0);
            var err = await UpdateService.DownloadAndInstallAsync(E.Api, release, progress);
            _updateText.Text = err ?? L.T("updateInstall");
            if (err is not null) await Ui.Alert(err);
        }
        finally
        {
            _updating = false;
            _updateBtn.IsEnabled = true;
            Render();
        }
    }

    private async Task SignOutAsync()
    {
        if (!await Ui.Confirm(L.T("signOut") + "?")) return;
        await E.SignOutAsync();
        MainActivity.StopTillService();
        Ui.Restart(new LoginPage());
    }
}
