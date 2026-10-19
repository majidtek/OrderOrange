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
