using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls;

namespace AiControlsSample;

/// <summary>
/// Custom inner content view for the create_plan tool approval card.
/// Parses the steps_json argument and renders a numbered list.
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

        // Extract steps_json argument
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

        List<string>? steps = null;
        try
        {
            steps = JsonSerializer.Deserialize<List<string>>(stepsJson);
        }
        catch (JsonException)
        {
            // Fallback: show raw text if not valid JSON
            Content = new Label { Text = stepsJson, LineBreakMode = LineBreakMode.WordWrap };
            return;
        }

        if (steps is null || steps.Count == 0)
        {
            Content = new Label { Text = "(empty plan)" };
            return;
        }

        var stack = new VerticalStackLayout { Spacing = 6 };

        for (int i = 0; i < steps.Count; i++)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };

            row.Add(new Label
            {
                Text = $"{i + 1}.",
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Start,
                TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Colors.LightGray : Colors.DimGray,
                MinimumWidthRequest = 24,
            });

            row.Add(new Label
            {
                Text = steps[i],
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill,
            });

            stack.Add(row);
        }

        Content = stack;
    }
}
