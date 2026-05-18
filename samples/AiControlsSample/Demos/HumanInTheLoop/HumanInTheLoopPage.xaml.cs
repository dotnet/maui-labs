using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class HumanInTheLoopPage : ContentPage
{
    public AgentContext Session { get; }

    private List<PlanStep>? _currentSteps;

    public HumanInTheLoopPage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(CreatePlan, "create_plan", "Create a plan with the given steps for the user to review."),
            AIFunctionFactory.Create(UpdatePlanStep, "update_plan_step", "Mark a plan step as completed. Step index is 0-based.")
        };

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a plan assistant. When the user asks you to do something:
                1. Create a plan by calling create_plan with a list of step descriptions.
                2. After the plan is shown, WAIT for the user to confirm or reject. Do NOT proceed until they reply.
                3. If the user confirms, execute each step one by one, calling update_plan_step to mark each as completed.
                4. If the user rejects, acknowledge and ask what they'd like to change.

                Always create 3-5 concrete steps that clearly describe what will happen.
                """,
            Tools = [.. tools]
        };
        var agent = new UIAgent(chatClient, chatOptions);
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    [Description("Create a plan with the given steps for the user to review.")]
    private string CreatePlan(
        [Description("JSON array of step descriptions, e.g. [\"Step 1\", \"Step 2\"]")] string steps_json)
    {
        var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];
        _currentSteps = steps.Select(s => new PlanStep { Description = s }).ToList();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlanPanel.IsVisible = true;
            ConfirmButtons.IsVisible = true;
            RefreshStepsUI();
        });

        return "Plan created and shown to user. Waiting for confirmation.";
    }

    [Description("Mark a plan step as completed by zero-based index.")]
    private string UpdatePlanStep(
        [Description("Zero-based step index to mark as completed")] int step_index)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_currentSteps is not null && step_index >= 0 && step_index < _currentSteps.Count)
            {
                _currentSteps[step_index].IsCompleted = true;
                RefreshStepsUI();
            }
        });
        return $"Step {step_index} marked as completed.";
    }

    private void RefreshStepsUI()
    {
        StepsLayout.Children.Clear();
        if (_currentSteps is null)
            return;

        for (int i = 0; i < _currentSteps.Count; i++)
        {
            var step = _currentSteps[i];
            var icon = step.IsCompleted ? "✅" : "⬜";
            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Children.Add(new Label { Text = icon, FontSize = 16, VerticalOptions = LayoutOptions.Center });
            row.Children.Add(new Label
            {
                Text = step.Description,
                FontSize = 14,
                VerticalOptions = LayoutOptions.Center,
                Opacity = step.IsCompleted ? 0.6 : 1.0
            });
            StepsLayout.Children.Add(row);
        }
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        ConfirmButtons.IsVisible = false;
        await Session.SendMessageAsync("I confirm the plan. Please proceed with execution.");
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        ConfirmButtons.IsVisible = false;
        await Session.SendMessageAsync("I reject this plan. Please suggest changes.");
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && _currentSteps is not null)
            PlanPanel.IsVisible = width >= 700;
    }

    private sealed class PlanStep
    {
        public string Description { get; init; } = string.Empty;
        public bool IsCompleted { get; set; }
    }
}
