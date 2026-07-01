using System.Text.Json;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AiControlsSample;

/// <summary>
/// Custom inner content for the create_plan approval card: parses the steps_json
/// argument and renders a numbered list instead of raw JSON.
/// </summary>
public class PlanStepsView : ContentView, IContentContextAware
{
    public void ApplyContentContext(ContentContext context)
    {
        if (context.Block is not FunctionApprovalBlock fab || fab.Arguments is null)
        {
            Content = new Label { Text = "(no plan steps)" };
            return;
        }

        string? stepsJson = null;
        foreach (var kvp in fab.Arguments)
        {
            if (string.Equals(kvp.Key, "steps_json", StringComparison.OrdinalIgnoreCase))
            {
                stepsJson = kvp.Value?.ToString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(stepsJson))
        {
            Content = new Label { Text = "(no steps provided)" };
            return;
        }

        List<string>? steps;
        try
        {
            steps = JsonSerializer.Deserialize<List<string>>(stepsJson);
        }
        catch (JsonException)
        {
            Content = new Label { Text = stepsJson, LineBreakMode = LineBreakMode.WordWrap };
            return;
        }

        if (steps is null || steps.Count == 0)
        {
            Content = new Label { Text = "(empty plan)" };
            return;
        }

        var stack = new VerticalStackLayout { Spacing = 6 };
        for (var i = 0; i < steps.Count; i++)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Add(new Label
            {
                Text = $"{i + 1}.",
                FontAttributes = FontAttributes.Bold,
                MinimumWidthRequest = 24,
            });
            row.Add(new Label
            {
                Text = steps[i],
                LineBreakMode = LineBreakMode.WordWrap,
                HorizontalOptions = LayoutOptions.Fill,
            });
            stack.Add(row);
        }

        Content = stack;
    }
}
