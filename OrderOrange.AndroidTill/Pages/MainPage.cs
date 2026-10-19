using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>The board: store, connection status, the red banner when a printer fails, and the last 20 jobs.</summary>
public sealed class MainPage : ContentPage
{
    private static TillEngine E => TillEngine.Instance;

    private readonly Label _store = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly Label _status = new() { FontSize = 14, TextColor = Colors.Gray };
    private readonly Label _banner = new()
    {
        BackgroundColor = Color.FromArgb("#C62828"), TextColor = Colors.White,
        Padding = new Thickness(10, 8), IsVisible = false, FontAttributes = FontAttributes.Bold,
    };
    private readonly Picker _stores = new() { IsVisible = false };
    private readonly CollectionView _jobs = new() { SelectionMode = SelectionMode.None };
    private readonly Label _noJobs = new() { TextColor = Colors.Gray };
    private bool _fillingStores;

    // Self-update: a green strip with the new version and an Update button, shown when the server has a newer APK.
    private readonly Label _updateText = new() { TextColor = Colors.White, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Button _updateBtn;
    private readonly Grid _update = new()
    {
        BackgroundColor = Color.FromArgb("#2E7D32"), Padding = new Thickness(10, 8), ColumnSpacing = 10, IsVisible = false,
        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
    };
    private bool _updating;

    public MainPage()
    {
        Title = L.T("appName");
        FlowDirection = Ui.Flow;

        _updateBtn = new Button { Text = L.T("updateNow"), BackgroundColor = Colors.White, TextColor = Color.FromArgb("#2E7D32"), CornerRadius = 8, Padding = new Thickness(12, 6), FontAttributes = FontAttributes.Bold };
        _updateBtn.Clicked += async (_, _) => await UpdateNowAsync();
        _update.Add(_updateText, 0, 0);
        _update.Add(_updateBtn, 1, 0);

        _stores.Title = L.T("switchStore");
        _stores.SelectedIndexChanged += async (_, _) =>
        {
            if (_fillingStores || _stores.SelectedIndex < 0 || _stores.SelectedIndex >= E.Stores.Count) return;
            var store = E.Stores[_stores.SelectedIndex];
            var err = await E.SwitchStoreAsync(store.Id);
            if (err is not null) { await Ui.Alert(err); Render(); }
        };

        _jobs.ItemTemplate = new DataTemplate(() =>
        {
            var time = new Label { FontSize = 12, TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Start, WidthRequest = 64 };
            time.SetBinding(Label.TextProperty, "Time");
            var title = new Label { FontAttributes = FontAttributes.Bold };
            title.SetBinding(Label.TextProperty, "Title");
            var printer = new Label { FontSize = 12, TextColor = Colors.Gray };
            printer.SetBinding(Label.TextProperty, "Printer");
            var result = new Label { FontSize = 12 };
            result.SetBinding(Label.TextProperty, "Result");
            result.SetBinding(Label.TextColorProperty, "Ok", converter: new OkColorConverter());
            var grid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                Padding = new Thickness(0, 5),
            };
            grid.Add(time, 0, 0);
            grid.Add(new VerticalStackLayout { Children = { title, printer, result } }, 1, 0);
            return grid;
        });

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(), new RowDefinition(), new RowDefinition(), new RowDefinition() },
            ColumnSpacing = 8, RowSpacing = 8,
        };
        buttons.Add(Ui.Primary(L.T("checkNow"), () => E.RefreshAsync()), 0, 0);
        buttons.Add(Ui.Primary(L.T("printers"), () => Navigation.PushAsync(new PrintersPage())), 1, 0);
        buttons.Add(Ui.Primary(L.T("routing"), () => Navigation.PushAsync(new RoutingPage())), 0, 1);
        buttons.Add(Ui.Secondary(L.T("reprintReceipt"), async () => { var e = await E.ReprintLastReceiptAsync(); if (e is not null) await Ui.Alert(e); }), 1, 1);
        buttons.Add(Ui.Secondary(L.T("reprintKitchen"), async () => { var e = await E.ReprintLastTicketsAsync(); if (e is not null) await Ui.Alert(e); }), 0, 2);
        buttons.Add(Ui.Secondary(L.T("settings"), () => Navigation.PushAsync(new SettingsPage())), 1, 2);
        var signOut = Ui.Danger(L.T("signOut"), SignOutAsync);
        buttons.Add(signOut, 0, 3);
        Grid.SetColumnSpan(signOut, 2);

        Content = new Grid
        {
            Padding = 16,
            RowSpacing = 8,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 6,
                    Children = { _store, _status, _update, _banner, _stores, buttons, Ui.Caption(L.T("jobs")), _noJobs },
                },
                _jobs,
            },
        };
        Grid.SetRow(_jobs, 1);
    }

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
        var run = E.Running ? L.T("running") : L.T("stopped");
        _status.Text = string.IsNullOrEmpty(E.Status) ? run : $"{run} · {E.Status}";
        _banner.Text = E.Banner ?? "";
        _banner.IsVisible = E.Banner is not null;
        if (!_updating)
        {
            var up = UpdateService.Available;
            _update.IsVisible = up is not null;
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
