using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// A fixed application-owned host for one named adaptive region.
/// </summary>
public sealed class AdaptiveRegionView : ContentView
{
    public AdaptiveRegionView(string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        Region = region;
    }

    public string Region { get; }

    public void Attach(AdaptiveSurfaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.RegisterRegionHost(this);
    }

    internal void SetAdaptiveContent(View? view) => Content = view;
}

internal sealed class AdaptiveSectionView : ContentView
{
    private readonly Label _title = new()
    {
        FontAttributes = FontAttributes.Bold,
        FontSize = 18,
    };
    private readonly ContentView _body = new();

    public AdaptiveSectionView()
    {
        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                _title,
                _body,
            },
        };
    }

    public string? Title
    {
        get => _title.Text;
        set
        {
            _title.Text = value;
            _title.IsVisible = !string.IsNullOrWhiteSpace(value);
        }
    }

    public View? Body
    {
        get => _body.Content;
        set => _body.Content = value;
    }
}

internal sealed class AdaptiveTabsView : ContentView
{
    private readonly HorizontalStackLayout _headers = new() { Spacing = 8 };
    private readonly ContentView _body = new();
    private IReadOnlyList<(string Title, View View)> _tabs = [];
    private int _selectedIndex;

    public AdaptiveTabsView()
    {
        Content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                _headers,
                _body,
            },
        };
    }

    public void SetTabs(IReadOnlyList<(string Title, View View)> tabs)
    {
        _tabs = tabs;
        _headers.Children.Clear();
        for (var index = 0; index < tabs.Count; index++)
        {
            var selectedIndex = index;
            var button = new Button { Text = tabs[index].Title };
            button.Clicked += (_, _) => Select(selectedIndex);
            _headers.Children.Add(button);
        }

        Select(Math.Min(_selectedIndex, Math.Max(0, tabs.Count - 1)));
    }

    public void ClearTabs()
    {
        _tabs = [];
        _headers.Children.Clear();
        _body.Content = null;
    }

    private void Select(int index)
    {
        _selectedIndex = index;
        _body.Content = _tabs.Count == 0 ? null : _tabs[index].View;
    }
}
