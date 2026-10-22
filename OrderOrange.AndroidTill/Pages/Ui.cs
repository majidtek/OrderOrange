using System.Globalization;
using OrderOrange.AndroidTill.Localization;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>The few widgets every page shares, so the screens stay short and look alike.</summary>
public static class Ui
{
    public static FlowDirection Flow => L.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public static Button Primary(string text, Func<Task> action) => Wire(new Button
    {
        Text = text,
        BackgroundColor = App.Orange,
        TextColor = Colors.White,
        CornerRadius = 8,
        Padding = new Thickness(12, 10),
    }, action);

    public static Button Secondary(string text, Func<Task> action) => Wire(new Button
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#EEEEEE"),
        TextColor = Colors.Black,
        CornerRadius = 8,
        Padding = new Thickness(12, 10),
    }, action);

    public static Button Danger(string text, Func<Task> action) => Wire(new Button
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#C62828"),
        TextColor = Colors.White,
        CornerRadius = 8,
        Padding = new Thickness(12, 10),
    }, action);

    /// <summary>A white, rounded card on the paper ground: how the home screen groups things.</summary>
    public static Border Card(View content, double padding = 14, bool shadow = true) => new()
    {
        Content = content,
        BackgroundColor = App.Card,
        Stroke = App.Line,
        StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
        Padding = new Thickness(padding),
        Shadow = shadow
            ? new Shadow { Brush = new SolidColorBrush(Color.FromArgb("#2B2118")), Opacity = 0.06f, Radius = 10, Offset = new Point(0, 2) }
            : null!,
    };

    /// <summary>
    /// One entry of the home screen menu: an icon on a tinted square above its name, the whole
    /// card tappable. Busy while its action runs, so a double tap cannot print twice.
    /// </summary>
    public static Border Tile(string icon, string text, Color tint, Func<Task> action)
    {
        var glyph = new Border
        {
            WidthRequest = 46, HeightRequest = 46,
            BackgroundColor = tint.WithAlpha(0.14f),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label { Text = icon, FontSize = 22, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        var name = new Label
        {
            Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = App.Ink,
            LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2,
        };
        var tile = Card(new VerticalStackLayout { Spacing = 10, Children = { glyph, name } }, 14);
        tile.MinimumHeightRequest = 104;

        var busy = false;
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            tile.Opacity = 0.55;
            try { await action(); }
            catch (Exception ex) { await Alert(ex.Message); }
            finally { tile.Opacity = 1; busy = false; }
        };
        tile.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(tile, text);
        return tile;
    }

    /// <summary>A small rounded label with a coloured dot: Running, Stopped, a printer's state.</summary>
    public static Border Pill(Label text, BoxView dot) => new()
    {
        BackgroundColor = App.Paper,
        StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
        Padding = new Thickness(10, 4),
        HorizontalOptions = LayoutOptions.Start,
        Content = new HorizontalStackLayout { Spacing = 6, Children = { dot, text } },
    };

    private static Button Wire(Button b, Func<Task> action)
    {
        b.Clicked += async (_, _) =>
        {
            b.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { await Alert(ex.Message); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }

    public static Label Caption(string text) => new()
    {
        Text = text,
        FontSize = 13,
        TextColor = Colors.Gray,
        Margin = new Thickness(0, 8, 0, 0),
    };

    public static Label Title(string text) => new()
    {
        Text = text,
        FontSize = 24,
        FontAttributes = FontAttributes.Bold,
        TextColor = App.Orange,
    };

    public static Grid Row(string caption, View control)
    {
        var g = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(0, 6) };
        g.Add(new Label { Text = caption, VerticalOptions = LayoutOptions.Center }, 0, 0);
        g.Add(control, 1, 0);
        return g;
    }

    /// <summary>Replaces the whole navigation stack — after sign-in, sign-out or a language change.</summary>
    public static void Restart(Page page)
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is null) return;
        window.Page = new NavigationPage(page) { BarBackgroundColor = App.Orange, BarTextColor = Colors.White };
    }

    public static Task Alert(string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is null ? Task.CompletedTask : page.DisplayAlert(L.T("appName"), message, L.T("ok"));
    }

    public static async Task<bool> Confirm(string question)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is not null && await page.DisplayAlert(L.T("appName"), question, L.T("yes"), L.T("no"));
    }
}

/// <summary>Green for a job that printed, red for one that did not.</summary>
public sealed class OkColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Color.FromArgb("#2E7D32") : Color.FromArgb("#C62828");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
