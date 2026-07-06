using System.Text.Json;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders a <see cref="FunctionInvocationContentBlock"/> (any tool that isn't projected into a richer
/// block, e.g. cart, orders, navigation, reviews) as a compact, tap-to-expand row: tool name up top,
/// and — once expanded — its arguments and result. Mirrors the Garden sample's original tool card.
/// </summary>
public sealed class GardenToolView : ContentContextView
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private bool _expanded;

    public GardenToolView()
    {
        Content = new VerticalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.Start };
    }

    protected override void RefreshFromContentContext()
    {
        var root = (VerticalStackLayout)Content;
        root.Children.Clear();

        if (ContentContext?.Block is not FunctionInvocationContentBlock block)
            return;

        var argsText = BuildArgs(block);
        var resultText = BuildResult(block);
        var hasDetails = argsText is not null || resultText is not null;

        root.Add(BuildHeader(block, hasDetails));

        if (_expanded && hasDetails)
            root.Add(BuildDetails(argsText, resultText));
    }

    private View BuildHeader(FunctionInvocationContentBlock block, bool hasDetails)
    {
        var chevron = new Label
        {
            Text = _expanded ? FluentIcons.ChevronDown : FluentIcons.ChevronRight,
            FontFamily = "FluentFilled",
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 14,
            IsVisible = hasDetails,
        };
        SetTheme(chevron, Label.TextColorProperty, "TextTertiaryLight", "#7A8A7A", "TextTertiaryDark", "#6A8068");

        var wrench = new Label
        {
            Text = FluentIcons.Wrench,
            FontFamily = "FluentFilled",
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
        };
        SetTheme(wrench, Label.TextColorProperty, "TextSecondaryLight", "#5A6A5A", "TextSecondaryDark", "#8AA08A");

        var name = new Label
        {
            Text = block.HasResult ? block.ToolName : $"{block.ToolName}…",
            FontSize = 12,
            FontAttributes = FontAttributes.Italic,
            VerticalOptions = LayoutOptions.Center,
        };
        SetTheme(name, Label.TextColorProperty, "TextSecondaryLight", "#5A6A5A", "TextSecondaryDark", "#8AA08A");

        var row = new HorizontalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(4, 2),
            Children = { chevron, wrench, name },
        };

        if (hasDetails)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _expanded = !_expanded;
                RefreshFromContentContext();
            };
            row.GestureRecognizers.Add(tap);
        }

        return row;
    }

    private View BuildDetails(string? argsText, string? resultText)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };

        if (argsText is not null)
        {
            stack.Add(SectionLabel("Arguments"));
            stack.Add(BodyLabel(argsText));
        }

        if (resultText is not null)
        {
            stack.Add(SectionLabel("Result"));
            stack.Add(BodyLabel(resultText));
        }

        var border = new Border
        {
            Margin = new Thickness(18, 4, 0, 4),
            Padding = new Thickness(10, 8),
            MaximumHeightRequest = 300,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new ScrollView { Content = stack },
        };
        SetTheme(border, Border.BackgroundColorProperty, "SurfaceLight", "#F6FAF5", "SurfaceDark", "#2A3A2A");
        SetTheme(border, Border.StrokeProperty, "CardStrokeLight", "#D5E5D2", "CardStrokeDark", "#3A4A38");
        return border;
    }

    private static Label SectionLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
        };
        SetTheme(label, Label.TextColorProperty, "TextTertiaryLight", "#7A8A7A", "TextTertiaryDark", "#6A8068");
        return label;
    }

    private static Label BodyLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 12,
            FontFamily = "Courier New",
            LineBreakMode = LineBreakMode.WordWrap,
        };
        SetTheme(label, Label.TextColorProperty, "TextSecondaryLight", "#5A6A5A", "TextSecondaryDark", "#8AA08A");
        return label;
    }

    private static string? BuildArgs(FunctionInvocationContentBlock block)
    {
        if (block.Arguments is not { Count: > 0 } args)
            return null;
        return string.Join("\n", args.Select(kv => $"{kv.Key}: {kv.Value}"));
    }

    private static string? BuildResult(FunctionInvocationContentBlock block)
    {
        var result = block.Result?.Result;
        if (result is null)
            return null;

        try
        {
            return result switch
            {
                string s => s,
                _ => JsonSerializer.Serialize(result, JsonOptions),
            };
        }
        catch
        {
            return result.ToString();
        }
    }

    private static void SetTheme(VisualElement element, BindableProperty property,
        string lightKey, string lightFallback, string darkKey, string darkFallback)
    {
        element.SetAppThemeColor(property, Res(lightKey, lightFallback), Res(darkKey, darkFallback));
    }

    private static Color Res(string key, string fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Color.FromArgb(fallback);
}
