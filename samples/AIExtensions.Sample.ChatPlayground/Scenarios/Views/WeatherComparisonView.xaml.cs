using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows a <see cref="WeatherComparisonBlock"/>: one row per city, filled in as each result arrives.</summary>
public partial class WeatherComparisonView : ScenarioCardView
{
    public WeatherComparisonView() => InitializeComponent();

    protected override void Refresh()
    {
        if (RowsLayout is null || Block is not WeatherComparisonBlock comparison)
            return;

        var running = IsRunning(comparison);
        var rows = comparison.Rows;
        var done = comparison.CompletedCount;
        var warmest = rows.Where(static row => row.Report is not null).MaxBy(static row => row.Report!.Temperature);

        var pending = rows.FirstOrDefault(static row => !row.IsComplete);
        var reported = rows.Count(static row => row.Report is not null);
        HeadlineLabel.Text = running && pending is not null ? $"Checking {pending.City}\u2026"
            : warmest is null ? $"{rows.Count} {(rows.Count == 1 ? "city" : "cities")}"
            : reported == 1 ? $"{warmest.City}, {warmest.Report!.Temperature}\u00B0C"
            : $"{(running ? "Warmest so far" : "Warmest")}: {warmest.City}, {warmest.Report!.Temperature}\u00B0C";
        ProgressLabel.Text = done == rows.Count ? $"{rows.Count} checked" : $"{done} of {rows.Count} checked";
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = running && done < rows.Count;

        RowsLayout.Children.Clear();
        for (var index = 0; index < rows.Count; index++)
        {
            if (index > 0)
                RowsLayout.Children.Add(Separator());
            RowsLayout.Children.Add(CreateRow(rows[index], running, IsStopped(comparison), index));
        }

        SemanticProperties.SetDescription(Card, $"Weather comparison. {HeadlineLabel.Text}. {ProgressLabel.Text}.");
    }

    private static View CreateRow(WeatherComparisonRow row, bool running, bool stopped, int index)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new(36), new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 8),
            AutomationId = $"WeatherComparisonRow{index}",
        };

        var glyph = new Label
        {
            Text = row.Report is { } report ? WeatherGlyphs.For(report.Conditions) : "\U0001F321\uFE0F",
            FontSize = 22,
            Opacity = row.Report is null ? 0.5 : 1,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        AutomationProperties.SetIsInAccessibleTree(glyph, false);
        grid.Add(glyph, 0);

        var details = row.Report is { } weather
            ? $"{weather.Conditions} \u00B7 feels {weather.FeelsLike}\u00B0 \u00B7 wind {weather.WindSpeed} km/h"
            : row.IsComplete ? row.Error ?? "No result"
            : running ? "Checking\u2026"
            : stopped ? "Stopped" : "No result";
        var text = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Styled(new Label { Text = row.City, FontAttributes = FontAttributes.Bold }, AIChatThemeKeys.AssistantTextStyle),
                Styled(new Label { Text = details, FontSize = 12 }, AIChatThemeKeys.SecondaryTextStyle),
            },
        };
        grid.Add(text, 1);

        View value = row.Report is { } result
            ? Styled(new Label
            {
                Text = $"{result.Temperature}\u00B0C",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            }, AIChatThemeKeys.AssistantTextStyle)
            : new ActivityIndicator
            {
                IsRunning = running && !row.IsComplete,
                IsVisible = running && !row.IsComplete,
                WidthRequest = 16,
                HeightRequest = 16,
                VerticalOptions = LayoutOptions.Center,
            };
        if (value is ActivityIndicator indicator)
            indicator.SetDynamicResource(ActivityIndicator.ColorProperty, AIChatThemeKeys.SecondaryTextColor);
        grid.Add(value, 2);

        SemanticProperties.SetDescription(grid, row.Report is { } spoken
            ? $"{row.City}: {spoken.Temperature} degrees, {spoken.Conditions}"
            : $"{row.City}: {details}");
        return grid;
    }

    private static BoxView Separator()
    {
        var line = new BoxView { HeightRequest = 1 };
        line.SetDynamicResource(BoxView.ColorProperty, AIChatThemeKeys.StrokeColor);
        return line;
    }

    private static Label Styled(Label label, string styleKey)
    {
        label.SetDynamicResource(StyleProperty, styleKey);
        return label;
    }
}
