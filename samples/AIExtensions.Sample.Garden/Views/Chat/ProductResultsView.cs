using AIExtensions.Sample.Garden.Chat;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace AIExtensions.Sample.Garden.Views;

/// <summary>
/// Renders a <see cref="ProductResultsBlock"/>. Adapts to the number of products discovered in the turn:
/// exactly one becomes a single detail card, several become a horizontally-scrolling carousel, and none
/// (a not-found lookup) shows a friendly empty state. Rebuilds from the block on each streamed change.
/// </summary>
public sealed class ProductResultsView : ContentContextView
{
    private const double CompactCardWidth = 168;
    private const double DetailCardWidth = 320;

    private readonly Grid _root;

    public ProductResultsView()
    {
        _root = new Grid { Padding = new Thickness(0, 4) };
        Content = _root;
    }

    protected override void RefreshFromContentContext()
    {
        _root.Children.Clear();

        if (ContentContext?.Block is not ProductResultsBlock block)
            return;

        var products = block.Products;

        if (products.Count == 0)
        {
            if (block.AnyResultReceived)
                _root.Add(BuildEmpty());
            return;
        }

        if (products.Count == 1)
        {
            _root.Add(BuildDetailCard(products[0]));
            return;
        }

        _root.Add(BuildCarousel(products));
    }

    private View BuildCarousel(IReadOnlyList<Product> products)
    {
        var cards = new HorizontalStackLayout { Spacing = 10 };
        foreach (var product in products)
            cards.Add(BuildCompactCard(product));

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = cards,
        };
    }

    private View BuildCompactCard(Product product)
    {
        var glyph = new Label
        {
            Text = product.Emoji,
            FontFamily = "FluentFilled",
            FontSize = 30,
            HorizontalOptions = LayoutOptions.Start,
        };
        glyph.SetDynamicResource(Label.TextColorProperty, "Primary");

        var name = new Label
        {
            Text = product.Name,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        SetAppTheme(name, Label.TextColorProperty, "TextAccentLight", "#3A5A3A", "TextAccentDark", "#B4D4B4");

        var category = new Label { Text = product.Category, FontSize = 11 };
        SetAppTheme(category, Label.TextColorProperty, "TextMetaLight", "#555555", "TextMetaDark", "#AAAAAA");

        var price = new Label
        {
            Text = $"${product.Price:0.00}",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
        };
        SetAppTheme(price, Label.TextColorProperty, "TextAccentLight", "#3A5A3A", "TextAccentDark", "#B4D4B4");

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { glyph, name, category, price },
        };

        return Card(stack, CompactCardWidth);
    }

    private View BuildDetailCard(Product product)
    {
        var glyph = new Label
        {
            Text = product.Emoji,
            FontFamily = "FluentFilled",
            FontSize = 44,
            VerticalOptions = LayoutOptions.Center,
        };
        glyph.SetDynamicResource(Label.TextColorProperty, "Primary");
        Grid.SetRowSpan(glyph, 2);

        var name = new Label
        {
            Text = product.Name,
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        SetAppTheme(name, Label.TextColorProperty, "TextAccentLight", "#3A5A3A", "TextAccentDark", "#B4D4B4");
        Grid.SetColumn(name, 1);

        var category = new Label { Text = product.Category, FontSize = 12 };
        SetAppTheme(category, Label.TextColorProperty, "TextMetaLight", "#555555", "TextMetaDark", "#AAAAAA");
        Grid.SetColumn(category, 1);
        Grid.SetRow(category, 1);

        var price = new Label
        {
            Text = $"${product.Price:0.00}",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
        };
        price.SetDynamicResource(Label.TextColorProperty, "Primary");
        Grid.SetRow(price, 2);
        Grid.SetColumnSpan(price, 2);

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
            RowSpacing = 4,
            ColumnSpacing = 14,
            Children = { glyph, name, category, price },
        };

        return Card(grid, DetailCardWidth);
    }

    private View BuildEmpty()
    {
        var label = new Label
        {
            Text = "No matching products found. Try a different name or category.",
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        SetAppTheme(label, Label.TextColorProperty, "TextSecondaryLight", "#5A6A5A", "TextSecondaryDark", "#8AA08A");
        return Card(label, DetailCardWidth);
    }

    private static Border Card(View content, double width)
    {
        var border = new Border
        {
            WidthRequest = width,
            Padding = 14,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = content,
        };
        SetAppTheme(border, Border.BackgroundColorProperty, "CardBackgroundLight", "#FFFFFF", "CardBackgroundDark", "#243024");
        SetAppTheme(border, Border.StrokeProperty, "CardStrokeLight", "#D5E5D2", "CardStrokeDark", "#3A4A38");
        return border;
    }

    private static void SetAppTheme(VisualElement element, BindableProperty property,
        string lightKey, string lightFallback, string darkKey, string darkFallback)
    {
        element.SetAppThemeColor(property, Res(lightKey, lightFallback), Res(darkKey, darkFallback));
    }

    private static Color Res(string key, string fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Color.FromArgb(fallback);
}
