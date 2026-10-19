using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Models;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>The printers this terminal drives. Tap one to edit it, or add a new one.</summary>
public sealed class PrintersPage : ContentPage
{
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.Single };
    private readonly Label _empty = new() { TextColor = Colors.Gray, Margin = new Thickness(0, 12) };

    public PrintersPage()
    {
        Title = L.T("printers");
        FlowDirection = Ui.Flow;

        _list.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { FontAttributes = FontAttributes.Bold, FontSize = 16 };
            name.SetBinding(Label.TextProperty, "Name");
            var summary = new Label { FontSize = 13, TextColor = Colors.Gray };
            summary.SetBinding(Label.TextProperty, "Summary");
            return new VerticalStackLayout { Padding = new Thickness(0, 8), Children = { name, summary } };
        });
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not PrinterConfig p) return;
            _list.SelectedItem = null;
            await Navigation.PushAsync(new PrinterEditPage(p));
        };

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
                        // Finding comes first: the terminal's own printer and the Wi-Fi ones are
                        // detected, not typed (Majed 2026-10-18: "there is no printer").
                        Ui.Primary(L.T("findPrinters"), () => Navigation.PushAsync(new DiscoverPage())),
                        Ui.Secondary(L.T("addPrinter"), () => Navigation.PushAsync(new PrinterEditPage(null))),
                        _empty,
                    },
                },
                _list,
            },
        };
        Grid.SetRow(_list, 1);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var printers = AppSettings.Current.Printers.ToList();
        _list.ItemsSource = printers;
        _empty.Text = printers.Count == 0 ? L.T("noPrinters") : "";
        _empty.IsVisible = printers.Count == 0;
    }
}
