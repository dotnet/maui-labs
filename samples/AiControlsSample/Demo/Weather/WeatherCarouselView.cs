using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace AiControlsSample;

/// <summary>
/// Renders a <see cref="WeatherCollectionBlock"/> as a horizontally-scrolling row of weather cards —
/// one card per city. Rebuilds from the block's items on each change (a pending card fills in when its
/// result arrives). A <see cref="CarouselView"/> could be swapped in for paging; a horizontal
/// <see cref="ScrollView"/> is used here as it nests cleanly inside the message list.
/// </summary>
public sealed class WeatherCarouselView : ContentContextView
{
    private const double CardWidth = 240;

    private readonly HorizontalStackLayout _cards;

    public WeatherCarouselView()
    {
        _cards = new HorizontalStackLayout { Spacing = 10 };

        Content = new Grid
        {
            Padding = new Thickness(0, 4),
            Children =
            {
                new ScrollView
                {
                    Orientation = ScrollOrientation.Horizontal,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                    Content = _cards,
                },
            },
        };
    }

    protected override void RefreshFromContentContext()
    {
        _cards.Clear();

        if (ContentContext?.Block is not WeatherCollectionBlock block)
            return;

        foreach (var item in block.Items)
            _cards.Add(BuildCard(item));
    }

    private static View BuildCard(WeatherItem item)
    {
        var icon = new Label
        {
            Text = item.ConditionIcon ?? "🌡️",
            FontSize = 40,
            VerticalOptions = LayoutOptions.Center,
        };
        Grid.SetRowSpan(icon, 2);

        var city = new Label
        {
            Text = item.Location ?? item.City ?? "…",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
        };
        var condition = new Label
        {
            Text = item.HasResult ? (item.Conditions ?? "--") : "…",
            FontSize = 13,
            Opacity = 0.7,
        };
        var header = new VerticalStackLayout { Spacing = 2, Children = { city, condition } };
        Grid.SetColumn(header, 1);

        var temp = new Label
        {
            Text = item.HasResult ? $"{item.Temperature}°C" : "--",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
        };
        Grid.SetRow(temp, 1);
        Grid.SetColumn(temp, 1);

        var details = new HorizontalStackLayout
        {
            Spacing = 16,
            Children =
            {
                Detail("💧", item.HasResult ? $"{item.Humidity}%" : "--"),
                Detail("💨", item.HasResult ? $"{item.WindSpeed} km/h" : "--"),
                Detail("🌡️", item.HasResult ? $"feels {item.FeelsLike}°C" : "--"),
            },
        };
        Grid.SetRow(details, 2);
        Grid.SetColumnSpan(details, 2);

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            RowSpacing = 8,
            ColumnSpacing = 12,
            Children = { icon, header, temp, details },
        };

        var border = new Border
        {
            WidthRequest = CardWidth,
            Padding = 16,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = grid,
        };
        border.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#F0F9FF"), Color.FromArgb("#1E293B"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#E2E8F0"), Color.FromArgb("#334155"));
        return border;
    }

    private static View Detail(string glyph, string text) => new HorizontalStackLayout
    {
        Spacing = 4,
        Children =
        {
            new Label { Text = glyph, FontSize = 12, VerticalOptions = LayoutOptions.Center },
            new Label { Text = text, FontSize = 12, Opacity = 0.7, VerticalOptions = LayoutOptions.Center },
        },
    };
}
