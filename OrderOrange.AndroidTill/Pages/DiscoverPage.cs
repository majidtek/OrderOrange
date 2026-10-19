using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Printing;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>
/// "Find printers": sweeps the terminal and the shop Wi-Fi and lists what answers. Tapping a
/// result opens the printer form already filled in — the built-in printer as the cashier
/// (58 mm, no cut), a network printer as a kitchen printer (80 mm) — so the first set-up is
/// taps, not typing.
/// </summary>
public sealed class DiscoverPage : ContentPage
{
    private readonly Label _status = new() { TextColor = Colors.Gray, FontSize = 13 };
    private readonly ProgressBar _bar = new() { ProgressColor = App.Orange, IsVisible = false };
    private readonly VerticalStackLayout _results = new() { Spacing = 8 };
    private readonly Button _again;
    private CancellationTokenSource? _cts;

    public DiscoverPage()
    {
        Title = L.T("findPrinters");
        FlowDirection = Ui.Flow;
        _again = Ui.Secondary(L.T("scanAgain"), RunAsync);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 10,
                Children = { _status, _bar, _results, _again },
            },
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_results.Children.Count == 0) _ = RunAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cts?.Cancel();
    }

    private async Task RunAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _results.Children.Clear();
        _again.IsEnabled = false;
        _bar.IsVisible = true; _bar.Progress = 0;
        _status.Text = L.T("scanningDevice");
        try
        {
            var builtIn = await PrinterDiscovery.FindBuiltInAsync(ct);
            foreach (var f in builtIn) _results.Children.Add(Row(f));

            var local = PrinterDiscovery.LocalIPv4();
            if (local is null)
            {
                _status.Text = L.T("noWifi");
            }
            else
            {
                _status.Text = string.Format(L.T("scanningNet"), $"{local.Value.Address}/{local.Value.Prefix}");
                var progress = new Progress<int>(p => _bar.Progress = p / 100.0);
                var net = await PrinterDiscovery.ScanNetworkAsync(progress, ct);
                foreach (var f in net) _results.Children.Add(Row(f));
                _status.Text = _results.Children.Count == 0
                    ? L.T("foundNone")
                    : string.Format(L.T("foundCount"), _results.Children.Count);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _bar.IsVisible = false;
            _again.IsEnabled = true;
        }
    }

    private View Row(PrinterDiscovery.Found f)
    {
        var known = Models.AppSettings.Current.Printers.Any(p => p.PhysicalKey == f.Config.PhysicalKey);
        var title = new Label { Text = f.Title, FontAttributes = FontAttributes.Bold, FontSize = 16 };
        var detail = new Label { Text = f.Detail + (known ? $" · {L.T("alreadyAdded")}" : ""), FontSize = 13, TextColor = Colors.Gray, FlowDirection = FlowDirection.LeftToRight };
        var text = new VerticalStackLayout { Children = { title, detail }, VerticalOptions = LayoutOptions.Center };
        var use = Ui.Primary(L.T("useThis"), () => Navigation.PushAsync(new PrinterEditPage(null, f.Config)));
        var g = new Grid
        {
            Padding = new Thickness(12, 10),
            BackgroundColor = Color.FromArgb("#FFF4EC"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
        };
        g.Add(text, 0, 0);
        g.Add(use, 1, 0);
        return new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = g };
    }
}
