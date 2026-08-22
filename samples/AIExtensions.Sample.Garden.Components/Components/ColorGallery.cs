using System.Collections.Specialized;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.Layouts;

namespace AIExtensions.Sample.Garden.Components;

public sealed class ColorGallery : ProductComponentView
{
    private readonly FlexLayout _colors = new()
    {
        AutomationId = "ColorGalleryOptions",
        Direction = FlexDirection.Row,
        Wrap = FlexWrap.Wrap,
        JustifyContent = FlexJustify.Start,
        AlignItems = FlexAlignItems.Start,
    };
    private UiObjectCollection? _boundColors;

    public ColorGallery()
    {
        AutomationId = "ColorGallery";
        SemanticProperties.SetDescription(this, "Available product colors");

        Content = GardenComponentVisuals.Card(
            "ColorGalleryCard",
            new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("ColorGalleryHeading", "Available colors"),
                    _colors,
                },
            });

        BindingContextChanged += (_, _) => AttachColors();
    }

    protected override void OnVariantChanged() => RebuildColors();

    private void AttachColors()
    {
        if (_boundColors is not null)
            _boundColors.CollectionChanged -= OnColorsChanged;

        _boundColors = BindingContext is UiObject product
            ? UiObjectPath.ResolveDotted(product, "colorOptions.options")?.Children
            : null;

        if (_boundColors is not null)
            _boundColors.CollectionChanged += OnColorsChanged;

        RebuildColors();
    }

    private void OnColorsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildColors();

    private void RebuildColors()
    {
        _colors.Children.Clear();
        if (_boundColors is null)
            return;

        var gallery = string.Equals(Variant, "gallery", StringComparison.OrdinalIgnoreCase);
        foreach (var color in _boundColors)
            _colors.Children.Add(BuildColor(color, gallery));
    }

    private static View BuildColor(UiObject color, bool gallery)
    {
        var name = color.HasMember("name") ? color["name"].AsString() ?? "Color" : "Color";
        var hex = color.HasMember("hex") ? color["hex"].AsString() : null;
        var swatch = new Border
        {
            WidthRequest = gallery ? 104 : 48,
            HeightRequest = gallery ? 72 : 48,
            BackgroundColor = string.IsNullOrWhiteSpace(hex) ? Colors.Transparent : Color.FromArgb(hex),
            Stroke = new SolidColorBrush(GardenComponentVisuals.Stroke),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
        };

        var label = new Label
        {
            Text = name,
            FontSize = gallery ? 14 : 11,
            TextColor = GardenComponentVisuals.PrimaryText,
            HorizontalTextAlignment = TextAlignment.Center,
            MaximumWidthRequest = gallery ? 120 : 76,
        };

        var option = new VerticalStackLayout
        {
            AutomationId = $"ColorOption-{name.Replace(' ', '-')}",
            Spacing = 6,
            Margin = new Thickness(0, 0, 12, 12),
            Children = { swatch, label },
        };
        SemanticProperties.SetDescription(option, $"{name} color option");
        return option;
    }
}
