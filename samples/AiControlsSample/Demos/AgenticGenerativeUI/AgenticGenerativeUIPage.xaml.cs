using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class AgenticGenerativeUIPage : ContentPage
{
    public AgentContext Session { get; }

    private List<PlanStep>? _currentSteps;

    public AgenticGenerativeUIPage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(CreatePlan, "create_plan", "Create a plan with the given steps. Returns the plan ID."),
            AIFunctionFactory.Create(CompleteStep, "complete_step", "Mark a plan step as completed by zero-based index.")
        };

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are an auto-planner. When the user asks you to do something:
                1. Create a plan by calling create_plan with step descriptions.
                2. Immediately start executing each step, calling complete_step for each.
                3. Do NOT wait for user confirmation — just execute.
                4. After all steps are done, summarize what you accomplished.

                Create 3-5 concrete steps. Execute them one by one.
                """,
            Tools = [.. tools]
        };
        var agent = new UIAgent(chatClient, chatOptions);
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    [Description("Create a plan with the given steps. Returns the plan ID.")]
    private string CreatePlan(
        [Description("JSON array of step descriptions")] string steps_json)
    {
        var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];
        _currentSteps = steps.Select(s => new PlanStep { Description = s }).ToList();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlanFooter.IsVisible = true;
            RefreshStepsUI();
        });

        return $"Plan created with {_currentSteps.Count} steps.";
    }

    [Description("Mark a plan step as completed by zero-based index.")]
    private string CompleteStep(
        [Description("Zero-based step index to complete")] int step_index)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_currentSteps is not null && step_index >= 0 && step_index < _currentSteps.Count)
            {
                _currentSteps[step_index].IsCompleted = true;
                RefreshStepsUI();
            }
        });
        return $"Step {step_index} completed.";
    }

    private void RefreshStepsUI()
    {
        StepsLayout.Children.Clear();
        if (_currentSteps is null)
            return;

        for (int i = 0; i < _currentSteps.Count; i++)
        {
            var step = _currentSteps[i];
            var icon = step.IsCompleted ? "✅" : "⏳";
            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Children.Add(new Label { Text = icon, FontSize = 14, VerticalOptions = LayoutOptions.Center });
            row.Children.Add(new Label
            {
                Text = step.Description,
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center,
                Opacity = step.IsCompleted ? 0.6 : 1.0
            });
            StepsLayout.Children.Add(row);
        }
    }

    private sealed class PlanStep
    {
        public string Description { get; init; } = string.Empty;
        public bool IsCompleted { get; set; }
    }
}
