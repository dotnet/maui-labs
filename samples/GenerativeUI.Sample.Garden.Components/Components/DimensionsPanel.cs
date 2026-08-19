namespace GenerativeUI.Sample.Garden.Components;

public sealed class DimensionsPanel : ProductComponentView
{
    private readonly Label _width;
    private readonly Label _height;
    private readonly Label _depth;
    private readonly Label _unit;

    public DimensionsPanel()
    {
        AutomationId = "DimensionsPanel";
        SemanticProperties.SetDescription(this, "Product dimensions");

        _unit = new Label
        {
            AutomationId = "DimensionsUnit",
            FontSize = 13,
            TextColor = GardenComponentVisuals.SecondaryText,
            VerticalOptions = LayoutOptions.Center,
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                GardenComponentVisuals.SectionTitle("DimensionsHeading", "Dimensions"),
                _unit,
            },
        };
        Grid.SetColumn(_unit, 1);

        _width = MeasurementValue("DimensionWidth");
        _height = MeasurementValue("DimensionHeight");
        _depth = MeasurementValue("DimensionDepth");

        var measurements = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 10,
        };
        measurements.Add(Measurement("Width", _width), 0);
        measurements.Add(Measurement("Height", _height), 1);
        measurements.Add(Measurement("Depth", _depth), 2);

        Content = GardenComponentVisuals.Card(
            "DimensionsCard",
            new VerticalStackLayout
            {
                Spacing = 14,
                Children = { header, measurements },
            });

        BindingContextChanged += (_, _) => AttachBindings();
    }

    private void AttachBindings()
    {
        if (BindingContext is not Microsoft.Maui.AI.GenerativeUI.Binding.UiObject product)
            return;

        BindTo(_width, product, "dimensions.width");
        BindTo(_height, product, "dimensions.height");
        BindTo(_depth, product, "dimensions.depth");
        BindTo(_unit, product, "dimensions.unit");
    }

    private static View Measurement(string label, Label value)
    {
        return new Border
        {
            Padding = new Thickness(8, 12),
            BackgroundColor = Color.FromArgb("#EEF7F0"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    value,
                    new Label
                    {
                        Text = label,
                        FontSize = 12,
                        TextColor = GardenComponentVisuals.SecondaryText,
                        HorizontalTextAlignment = TextAlignment.Center,
                    },
                },
            },
        };
    }

    private static Label MeasurementValue(string automationId)
        => new()
        {
            AutomationId = automationId,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.Primary,
            HorizontalTextAlignment = TextAlignment.Center,
        };

    private static void BindTo(
        Label label,
        Microsoft.Maui.AI.GenerativeUI.Binding.UiObject product,
        string path)
    {
        var source = Microsoft.Maui.AI.GenerativeUI.Binding.UiObjectPath.ResolveDotted(product, path);
        label.SetBinding(
            Label.TextProperty,
            new Microsoft.Maui.Controls.Binding("Value", converter: InvariantValueConverter.Instance)
            {
                Source = source,
            });
    }
}
