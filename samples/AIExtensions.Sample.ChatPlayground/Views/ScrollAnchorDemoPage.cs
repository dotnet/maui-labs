using System.Collections.ObjectModel;
using System.ComponentModel;
using AIExtensions.Sample.Scrolling;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Exercises <see cref="ScrollAnchor"/> on a grouped CollectionView and on a ScrollView: append to, grow, stream into,
/// and clear their content, and jump back to the end. Opened from DevFlow (the <c>scroll-anchor-demo</c> action in
/// <see cref="ScrollAnchorDemoActions"/>); it isn't part of the playground's own UI.
/// </summary>
public sealed class ScrollAnchorDemoPage : ContentPage
{
    private static readonly string[] Words =
        "the anchor keeps the end of the list in view while this answer streams in one word at a time".Split(' ');

    private readonly ObservableCollection<ScrollAnchorDemoGroup> _groups = [];
    private readonly VerticalStackLayout _stack = new() { Spacing = 6, Padding = 8 };
    private readonly CollectionView _list;
    private readonly ScrollView _scroll;
    private readonly Label _status = new() { AutomationId = "AnchorDemoStatus", FontSize = 13 };
    private readonly Button _latest;
    private IDispatcherTimer? _stream;
    private int _items;
    private int _words;

    public ScrollAnchorDemoPage()
    {
        Title = "Scroll anchor";
        _list = new CollectionView
        {
            AutomationId = "AnchorDemoList",
            IsGrouped = true,
            ItemsSource = _groups,
            SelectionMode = SelectionMode.None,
            GroupHeaderTemplate = new DataTemplate(() =>
            {
                var header = new Label { FontAttributes = FontAttributes.Bold, Padding = new Thickness(8, 12, 8, 4) };
                header.SetBinding(Label.TextProperty, static (ScrollAnchorDemoGroup group) => group.Title);
                return header;
            }),
            ItemTemplate = new DataTemplate(() =>
            {
                var text = new Label();
                text.SetBinding(Label.TextProperty, static (ScrollAnchorDemoItem item) => item.Text);
                return new Border { Content = text, Padding = 8, Margin = new Thickness(8, 0, 8, 6) };
            }),
        };
        _scroll = new ScrollView { AutomationId = "AnchorDemoScroll", Content = _stack };
        ScrollAnchor.SetMode(_list, ScrollAnchorMode.End);
        ScrollAnchor.SetMode(_scroll, ScrollAnchorMode.End);
        _list.PropertyChanged += OnAnchorChanged;
        _scroll.PropertyChanged += OnAnchorChanged;

        _latest = Command("Jump to latest", "AnchorDemoLatest", () =>
        {
            ScrollAnchor.ScrollToEnd(_list);
            ScrollAnchor.ScrollToEnd(_scroll);
        });

        var commands = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Children =
            {
                Command("Add", "AnchorDemoAdd", Add),
                Command("Grow", "AnchorDemoGrow", Grow),
                Command("Stream", "AnchorDemoStream", ToggleStream),
                Command("New group", "AnchorDemoGroup", AddGroup),
                Command("Remove", "AnchorDemoRemove", RemoveLast),
                Command("Clear", "AnchorDemoClear", Clear),
                _latest,
                Command("Close", "AnchorDemoClose", () => _ = Navigation.PopModalAsync()),
            },
        };

        var lists = new Grid
        {
            RowDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            RowSpacing = 8,
        };
        lists.Add(Section("Grouped CollectionView", _list), 0, 0);
        lists.Add(Section("ScrollView", _scroll), 0, 1);

        var root = new Grid
        {
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
            RowSpacing = 8,
            Padding = new Thickness(12, 12, 12, 36),
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
        };
        root.Add(commands, 0, 0);
        root.Add(_status, 0, 1);
        root.Add(lists, 0, 2);
        Content = root;

        for (var group = 0; group < 3; group++)
        {
            AddGroup();
            for (var item = 0; item < 8; item++)
                Add();
        }

        UpdateStatus();
    }

    protected override void OnDisappearing()
    {
        _stream?.Stop();
        base.OnDisappearing();
    }

    private static Button Command(string text, string automationId, Action action)
    {
        var button = new Button { Text = text, AutomationId = automationId, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4) };
        button.Clicked += (_, _) => action();
        return button;
    }

    private static Border Section(string title, View content)
    {
        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        grid.Add(new Label { Text = title, FontSize = 12, Padding = new Thickness(8, 4) }, 0, 0);
        grid.Add(content, 0, 1);
        return new Border { Content = grid, Padding = 0 };
    }

    private void AddGroup()
    {
        _groups.Add(new ScrollAnchorDemoGroup($"Group {_groups.Count + 1}"));
        _stack.Add(new Label { Text = $"Group {_groups.Count}", FontAttributes = FontAttributes.Bold });
    }

    private void Add()
    {
        if (_groups.Count == 0)
            AddGroup();

        _items++;
        _groups[^1].Add(new ScrollAnchorDemoItem($"Item {_items}"));
        _stack.Add(new Label { Text = $"Item {_items}" });
    }

    private void Grow()
    {
        for (var word = 0; word < 6; word++)
            AppendWord();
    }

    private void AppendWord()
    {
        var next = Words[_words++ % Words.Length];
        if (_groups.LastOrDefault(group => group.Count > 0) is { } group)
            group[^1].Text += " " + next;
        if (_stack.Children.LastOrDefault() is Label label)
            label.Text += " " + next;
    }

    private void ToggleStream()
    {
        if (_stream is { IsRunning: true })
        {
            _stream.Stop();
            return;
        }

        _stream ??= CreateStream();
        _stream.Start();
    }

    private IDispatcherTimer CreateStream()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(120);
        timer.Tick += (_, _) => AppendWord();
        return timer;
    }

    private void RemoveLast()
    {
        if (_groups.LastOrDefault(group => group.Count > 0) is { } group)
            group.RemoveAt(group.Count - 1);
        if (_stack.Children.LastOrDefault() is IView last)
            _stack.Remove(last);
    }

    private void Clear()
    {
        _groups.Clear();
        _stack.Clear();
        _items = 0;
    }

    private void OnAnchorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == ScrollAnchor.IsAnchoredProperty.PropertyName)
            UpdateStatus();
    }

    private void UpdateStatus()
    {
        static string Describe(BindableObject view) => ScrollAnchor.GetIsAnchored(view) ? "following" : "scrolled away";
        _status.Text = $"List: {Describe(_list)} | ScrollView: {Describe(_scroll)}";
        _latest.IsEnabled = !ScrollAnchor.GetIsAnchored(_list) || !ScrollAnchor.GetIsAnchored(_scroll);
    }
}

/// <summary>A group of <see cref="ScrollAnchorDemoPage"/>'s list.</summary>
internal sealed class ScrollAnchorDemoGroup(string title) : ObservableCollection<ScrollAnchorDemoItem>
{
    public string Title { get; } = title;
}

/// <summary>An item of <see cref="ScrollAnchorDemoPage"/>'s list, whose text grows.</summary>
internal sealed class ScrollAnchorDemoItem(string text) : INotifyPropertyChanged
{
    private string _text = text;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }
}
