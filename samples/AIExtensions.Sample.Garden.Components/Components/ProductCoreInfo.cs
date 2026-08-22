namespace AIExtensions.Sample.Garden.Components;

public sealed class ProductCoreInfo : ProductComponentView
{
    private readonly Label _description;

    public ProductCoreInfo()
    {
        AutomationId = "ProductCoreInfo";
        SemanticProperties.SetDescription(this, "Core product information");

        var title = GardenComponentVisuals.SectionTitle("ProductCoreInfoHeading", "Product details");

        var price = new Label
        {
            AutomationId = "ProductCoreInfoPrice",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.Primary,
        };
        price.SetBinding(Label.TextProperty, Bind("price", CurrencyValueConverter.Instance));
        SemanticProperties.SetDescription(price, "Product price");

        var category = new Label
        {
            AutomationId = "ProductCoreInfoCategory",
            FontSize = 13,
            TextColor = GardenComponentVisuals.SecondaryText,
        };
        category.SetBinding(
            Label.TextProperty,
            Bind("category", PrefixedValueConverter.Instance, "Category: "));

        var stock = new Label
        {
            AutomationId = "ProductCoreInfoStock",
            FontSize = 13,
            TextColor = GardenComponentVisuals.SecondaryText,
        };
        stock.SetBinding(Label.TextProperty, Bind("quantity", StockValueConverter.Instance));

        _description = new Label
        {
            AutomationId = "ProductCoreInfoDescription",
            FontSize = 16,
            LineHeight = 1.25,
            TextColor = GardenComponentVisuals.PrimaryText,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        _description.SetBinding(Label.TextProperty, Bind("description"));

        Content = GardenComponentVisuals.Card(
            "ProductCoreInfoCard",
            new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    title,
                    price,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { category, stock },
                    },
                    _description,
                },
            });
    }

    protected override void OnVariantChanged()
        => _description.MaxLines = string.Equals(Variant, "compact", StringComparison.OrdinalIgnoreCase)
            ? 3
            : -1;
}
