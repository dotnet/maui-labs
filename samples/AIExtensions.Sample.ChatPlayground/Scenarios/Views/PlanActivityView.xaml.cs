using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows a <see cref="PlanActivityBlock"/>: what is done, what the agent works on, and what is next.</summary>
public partial class PlanActivityView : ScenarioCardView
{
    public PlanActivityView() => InitializeComponent();

    protected override void Refresh()
    {
        if (StepsLayout is null || Block is not PlanActivityBlock plan)
            return;

        var active = plan.LifecycleState == BlockLifecycleState.Active && IsRunning(plan);
        var steps = plan.Steps;
        var done = plan.CompletedCount;
        var current = steps.FirstOrDefault(static step => step.IsRunning && !step.IsCompleted);
        var next = steps.FirstOrDefault(static step => !step.IsCompleted);

        CaptionLabel.Text = plan.IsDone ? "PLAN COMPLETE" : active ? "WORKING ON THE PLAN" : "PLAN";
        HeadlineLabel.Text = plan.IsDone ? "Every step is done"
            : current is not null ? current.Description
            : active && next is not null && done > 0 ? $"Next: {next.Description}"
            : active ? "Planning\u2026"
            : IsStopped(plan) ? "Stopped before the plan was done"
            : $"{steps.Count - done} {(steps.Count - done == 1 ? "step" : "steps")} left";
        CountLabel.Text = $"{done}/{steps.Count}";
        Bar.Progress = steps.Count == 0 ? 0 : (double)done / steps.Count;

        StepsLayout.Children.Clear();
        for (var index = 0; index < steps.Count; index++)
            StepsLayout.Children.Add(CreateStep(steps[index], active, index));

        SemanticProperties.SetDescription(Card, $"Plan, {done} of {steps.Count} steps done. {HeadlineLabel.Text}.");
    }

    private static View CreateStep(PlanStep step, bool active, int index)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new(22), new(GridLength.Star) },
            ColumnSpacing = 10,
            AutomationId = $"PlanActivityStep{index}",
        };

        var running = active && step.IsRunning && !step.IsCompleted;
        View marker;
        if (running)
        {
            var indicator = new ActivityIndicator
            {
                IsRunning = true,
                WidthRequest = 16,
                HeightRequest = 16,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };
            indicator.SetDynamicResource(ActivityIndicator.ColorProperty, AIChatThemeKeys.AccentColor);
            marker = indicator;
        }
        else
        {
            var glyph = new Label
            {
                Text = step.IsCompleted ? "\u2713" : "\u25CB",
                FontSize = step.IsCompleted ? 15 : 13,
                FontAttributes = step.IsCompleted ? FontAttributes.Bold : FontAttributes.None,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            glyph.SetDynamicResource(Label.TextColorProperty,
                step.IsCompleted ? AIChatThemeKeys.SuccessColor : AIChatThemeKeys.SecondaryTextColor);
            marker = glyph;
        }

        AutomationProperties.SetIsInAccessibleTree(marker, false);
        grid.Add(marker, 0);

        var text = new Label { Text = step.Description, VerticalOptions = LayoutOptions.Center };
        text.SetDynamicResource(StyleProperty,
            step.IsCompleted ? AIChatThemeKeys.SecondaryTextStyle : AIChatThemeKeys.AssistantTextStyle);
        if (running)
            text.FontAttributes = FontAttributes.Bold;
        grid.Add(text, 1);

        SemanticProperties.SetDescription(grid,
            $"Step {index + 1}, {(step.IsCompleted ? "done" : running ? "in progress" : "pending")}: {step.Description}");
        return grid;
    }
}
