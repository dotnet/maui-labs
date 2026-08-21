namespace GenerativeUI.Sample.Garden.Components;

public sealed class SeedGrowingTimeline : ProductComponentView
{
    private readonly Label _planting;
    private readonly Label _germination;
    private readonly Label _harvest;

    public SeedGrowingTimeline()
    {
        AutomationId = "SeedGrowingTimeline";
        SemanticProperties.SetDescription(this, "Seed planting and growing timeline");

        _planting = StepValue("SeedPlantingStepValue");
        _germination = StepValue("SeedGerminationStepValue");
        _harvest = StepValue("SeedHarvestStepValue");

        Content = GardenComponentVisuals.Card(
            "SeedGrowingTimelineCard",
            new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("SeedGrowingTimelineHeading", "Growing timeline"),
                    Step("1", "Plant", _planting, "SeedPlantingStep"),
                    Step("2", "Germinate", _germination, "SeedGerminationStep"),
                    Step("3", "Harvest", _harvest, "SeedHarvestStep"),
                },
            });

        BindingContextChanged += (_, _) => AttachBindings();
    }

    private void AttachBindings()
    {
        if (BindingContext is not Microsoft.Maui.AI.GenerativeUI.Binding.UiObject product)
            return;

        BindTo(_planting, product, "seedDetails.plantingInstructions");
        BindTo(_germination, product, "seedDetails.germinationWindow");
        BindTo(_harvest, product, "seedDetails.harvestWindow");
    }

    private static View Step(string number, string label, Label detail, string automationId)
    {
        return new Grid
        {
            AutomationId = automationId,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(40)),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 12,
            Children =
            {
                new Border
                {
                    WidthRequest = 36,
                    HeightRequest = 36,
                    BackgroundColor = GardenComponentVisuals.Primary,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
                    Content = new Label
                    {
                        Text = number,
                        TextColor = Colors.White,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                    },
                },
                new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = label,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = GardenComponentVisuals.PrimaryText,
                        },
                        detail,
                    },
                },
            },
        }.WithSecondChildInColumnOne();
    }

    private static Label StepValue(string automationId)
        => new()
        {
            AutomationId = automationId,
            FontSize = 14,
            TextColor = GardenComponentVisuals.SecondaryText,
            LineBreakMode = LineBreakMode.WordWrap,
        };

    private static void BindTo(
        Label label,
        Microsoft.Maui.AI.GenerativeUI.Binding.UiObject product,
        string path)
    {
        var source = Microsoft.Maui.AI.GenerativeUI.Binding.UiObjectPath.ResolveDotted(product, path);
        if (source is null)
        {
            label.RemoveBinding(Label.TextProperty);
            label.Text = string.Empty;
            return;
        }

        label.SetBinding(Label.TextProperty, new Microsoft.Maui.Controls.Binding("Value") { Source = source });
    }
}

internal static class SeedTimelineGridExtensions
{
    public static Grid WithSecondChildInColumnOne(this Grid grid)
    {
        grid.SetColumn(grid.Children[1], 1);
        return grid;
    }
}
