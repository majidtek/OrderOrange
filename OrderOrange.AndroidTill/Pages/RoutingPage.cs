using Microsoft.Maui.ApplicationModel;
using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Models;
using OrderOrange.AndroidTill.Services;
using OrderOrange.Shared;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>
/// Product printers — the Windows till's routing grid on Android (Majed 2026-10-18: "we need
/// set which product for which printer … make it same to windows"). One row per product, one
/// tick per printer set up on this terminal; a category header ticks a whole category at
/// once. Saved locally: a product's own ticks win, a category's shared ticks become the
/// category rule for dishes added later, and anything unticked follows the web map
/// (Kitchen/Bar) as before.
/// </summary>
public sealed class RoutingPage : ContentPage
{
    private readonly Entry _search = new() { ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
    private readonly Label _status = new() { TextColor = Colors.Gray, FontSize = 13 };
    private readonly VerticalStackLayout _rows = new() { Spacing = 2 };
    private readonly Button _save;
    private readonly List<PrinterConfig> _printers;
    private List<MenuCategoryDto> _menu = [];
    /// <summary>Product id → ticked printer ids (the working copy, saved on Save).</summary>
    private readonly Dictionary<int, HashSet<string>> _ticks = [];
    private bool _dirty;

    public RoutingPage()
    {
        Title = L.T("routing");
        FlowDirection = Ui.Flow;
        _printers = AppSettings.Current.Printers.Where(p => p.Enabled).ToList();
        _search.Placeholder = L.T("routingSearch");
        _search.TextChanged += (_, _) => Render();
        _save = Ui.Primary(L.T("save"), SaveAsync);

        var legend = new HorizontalStackLayout { Spacing = 10 };
        for (var i = 0; i < _printers.Count; i++)
            legend.Children.Add(new Label { Text = $"{i + 1} = {_printers[i].Name}", FontSize = 12, TextColor = Colors.Gray });

        Content = new Grid
        {
            Padding = 16,
            RowSpacing = 8,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
        };
        var head = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = L.T("routingHint"), FontSize = 13, TextColor = Colors.Gray },
                new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = legend },
                _search,
                _status,
            },
        };
        ((Grid)Content).Add(head, 0, 0);
        ((Grid)Content).Add(new ScrollView { Content = _rows }, 0, 1);
        ((Grid)Content).Add(_save, 0, 2);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_menu.Count > 0) return;
        if (_printers.Count == 0) { _status.Text = L.T("noPrinters"); _save.IsEnabled = false; return; }
        _status.Text = L.T("loadingMenu");
        try
        {
            _menu = await TillEngine.Instance.Api.MenuAsync();
            var s = AppSettings.Current;
            foreach (var cat in _menu)
                foreach (var item in cat.Items)
                {
                    var ids = s.ProductPrinters.TryGetValue(item.Id, out var own) && own.Count > 0 ? own
                            : s.CategoryPrinters.GetValueOrDefault(cat.Id) ?? [];
                    _ticks[item.Id] = ids.Where(id => _printers.Any(p => p.Id == id)).ToHashSet();
                }
            _status.Text = "";
            Render();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private bool Matches(MenuCategoryDto cat, MenuItemDto item)
    {
        var q = (_search.Text ?? "").Trim();
        return q.Length == 0
            || item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.NameFor(L.Lang).Contains(q, StringComparison.OrdinalIgnoreCase)
            || cat.NameFor(L.Lang).Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rebuilds the list — menus are a few hundred rows at most, so a plain rebuild is fine.</summary>
    private void Render()
    {
        _rows.Children.Clear();
        var shown = 0;
        foreach (var cat in _menu)
        {
            var items = cat.Items.Where(i => Matches(cat, i)).ToList();
            if (items.Count == 0) continue;
            _rows.Children.Add(CategoryRow(cat, items));
            foreach (var item in items) { _rows.Children.Add(ItemRow(item)); shown++; }
        }
        if (shown == 0 && _menu.Count > 0)
            _rows.Children.Add(new Label { Text = L.T("foundNone0"), TextColor = Colors.Gray, Margin = new Thickness(0, 12) });
    }

    private Grid Columns()
    {
        var g = new Grid { ColumnSpacing = 4, Padding = new Thickness(0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        foreach (var _ in _printers) g.ColumnDefinitions.Add(new ColumnDefinition(44));
        return g;
    }

    /// <summary>Category header: name + one "all of this category" tick per printer.</summary>
    private View CategoryRow(MenuCategoryDto cat, List<MenuItemDto> items)
    {
        var g = Columns();
        g.BackgroundColor = Color.FromArgb("#FFF4EC");
        g.Padding = new Thickness(8, 6);
        g.Add(new Label { Text = $"{cat.NameFor(L.Lang)} ({items.Count})", FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }, 0, 0);
        for (var i = 0; i < _printers.Count; i++)
        {
            var printer = _printers[i];
            var box = new CheckBox { Color = App.Orange, IsChecked = items.All(it => _ticks[it.Id].Contains(printer.Id)), HorizontalOptions = LayoutOptions.Center };
            box.CheckedChanged += (_, e) =>
            {
                foreach (var it in items) { if (e.Value) _ticks[it.Id].Add(printer.Id); else _ticks[it.Id].Remove(printer.Id); }
                Touch();
                Render();   // the item rows under it must follow
            };
            g.Add(new VerticalStackLayout { Children = { new Label { Text = (i + 1).ToString(), FontSize = 11, HorizontalTextAlignment = TextAlignment.Center, TextColor = Colors.Gray }, box } }, i + 1, 0);
        }
        return g;
    }

    private View ItemRow(MenuItemDto item)
    {
        var g = Columns();
        g.Padding = new Thickness(8, 2);
        var ticks = _ticks[item.Id];
        var name = new Label { Text = item.NameFor(L.Lang), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        if (ticks.Count == 0) name.TextColor = Colors.Gray;   // follows the web map
        g.Add(name, 0, 0);
        for (var i = 0; i < _printers.Count; i++)
        {
            var printer = _printers[i];
            var box = new CheckBox { Color = App.Orange, IsChecked = ticks.Contains(printer.Id), HorizontalOptions = LayoutOptions.Center };
            box.CheckedChanged += (_, e) =>
            {
                if (e.Value) ticks.Add(printer.Id); else ticks.Remove(printer.Id);
                name.TextColor = ticks.Count == 0 ? Colors.Gray : Colors.Black;
                Touch();
            };
            g.Add(box, i + 1, 0);
        }
        return g;
    }

    private void Touch()
    {
        _dirty = true;
        _status.Text = L.T("unsaved");
    }

    private Task SaveAsync()
    {
        var s = AppSettings.Current;
        s.ProductPrinters.Clear();
        foreach (var (productId, ids) in _ticks)
            if (ids.Count > 0) s.ProductPrinters[productId] = ids.ToList();

        // The printers EVERY product of a category shares become the category rule, so a
        // new dish added later on the web lands on those printers without being touched here.
        s.CategoryPrinters.Clear();
        foreach (var cat in _menu.Where(c => c.Items.Count > 0))
        {
            var common = cat.Items.Select(it => _ticks[it.Id].ToHashSet())
                .Aggregate((a, b) => { a.IntersectWith(b); return a; });
            if (common.Count > 0) s.CategoryPrinters[cat.Id] = common.ToList();
        }
        s.Save();
        _dirty = false;
        _status.Text = L.T("saved");
        return Task.CompletedTask;
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_dirty) return base.OnBackButtonPressed();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (await Ui.Confirm(L.T("discardChanges"))) await Navigation.PopAsync();
        });
        return true;
    }
}
