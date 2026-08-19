namespace GenerativeUI.Sample.Garden.Components;

public sealed class ProductHero : ProductComponentView
{
    private readonly Image _image;
    private readonly Label _name;

    public ProductHero()
    {
        AutomationId = "ProductHero";
        SemanticProperties.SetDescription(this, "Product hero");

        _image = new Image
        {
            AutomationId = "ProductHeroImage",
            Aspect = Aspect.AspectFill,
            HeightRequest = 220,
            HorizontalOptions = LayoutOptions.Fill,
        };
        _image.SetBinding(Image.SourceProperty, Bind("imageUrl"));

        var emoji = new Label
        {
            AutomationId = "ProductHeroEmoji",
            FontSize = 36,
            Margin = 12,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
        };
        emoji.SetBinding(Label.TextProperty, Bind("emoji"));

        _name = new Label
        {
            AutomationId = "ProductHeroName",
            FontSize = 32,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            Margin = new Thickness(20, 0, 20, 18),
            VerticalOptions = LayoutOptions.End,
        };
        _name.SetBinding(Label.TextProperty, Bind("name"));
        SemanticProperties.SetHeadingLevel(_name, SemanticHeadingLevel.Level1);

        var imageLayer = new Grid
        {
            Clip = new Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry
            {
                CornerRadius = 20,
                Rect = new Rect(0, 0, 1000, 260),
            },
            Children =
            {
                _image,
                new BoxView
                {
                    Background = new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new(Colors.Transparent, 0),
                            new(Color.FromArgb("#AA173C34"), 1),
                        },
                        new Point(0.5, 0),
                        new Point(0.5, 1)),
                },
                emoji,
                _name,
            },
        };

        Content = GardenComponentVisuals.Card(
            "ProductHeroCard",
            imageLayer,
            padding: 0);
    }

    protected override void OnVariantChanged()
    {
        var compact = string.Equals(Variant, "compact", StringComparison.OrdinalIgnoreCase);
        _image.HeightRequest = compact ? 140 : 220;
        _name.FontSize = compact ? 24 : 32;
    }
}
