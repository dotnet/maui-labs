using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AiControlsSample;

public partial class HumanInTheLoopPage : ContentPage
{
    private readonly IAgentSession _session;
    private Plan? _currentPlan;

    public HumanInTheLoopPage(IAgentSessionFactory sessionFactory, IChatClient chatClient)
    {
        InitializeComponent();

        _session = sessionFactory.Create(chatClient);
        _session.SystemInstructions = """
            You are a plan assistant. When the user asks you to do something:
            1. Create a plan by calling create_plan with a list of step descriptions.
            2. IMMEDIATELY call confirm_plan in the same response (do NOT wait for a separate user message).
               confirm_plan is a blocking tool — it pauses and waits for the user to click Confirm or Reject in the UI.
            3. If confirmed, execute each step one by one, calling update_plan_step to mark each as completed.
               Add a brief delay description for each step to make it feel realistic.
            4. If rejected, acknowledge and ask what they'd like to change.
            
            IMPORTANT: Always call create_plan AND confirm_plan together in the same tool_calls response.
            Always create 3-5 concrete steps that clearly describe what will happen.
            """;

        RegisterTools();

        ChatView.Session = _session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Simple plan", "Create a plan to organize my desk"),
            new Suggestion("Complex plan", "Create a plan to build a web application with authentication"),
            new Suggestion("Fun plan", "Create a plan to throw a surprise birthday party"),
        ];
    }

    private void RegisterTools()
    {
        [Description("Create a plan with the given steps for the user to review.")]
        Plan create_plan(
            [Description("JSON array of step descriptions, e.g. [\"Step 1\", \"Step 2\"]")] string steps_json)
        {
            var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];
            var plan = new Plan
            {
                Steps = steps.Select(s => new Step { Description = s }).ToList()
            };

            _currentPlan = plan;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PlanCard.Plan = plan;
                PlanCard.IsVisible = true;
                PlanCard.RefreshSteps();
            });
            return plan;
        }

        [Description("Wait for the user to confirm or reject the plan. Returns whether they confirmed.")]
        async Task<PlanConfirmationResult> confirm_plan()
        {
            MainThread.BeginInvokeOnMainThread(() => PlanCard.ShowConfirmation = true);
            var response = await _session.WaitForResponse("confirm_plan");
            MainThread.BeginInvokeOnMainThread(() => PlanCard.ShowConfirmation = false);
            return response as PlanConfirmationResult ?? new PlanConfirmationResult { Confirmed = false };
        }

        [Description("Mark a plan step as completed. Step index is 0-based.")]
        string update_plan_step(
            [Description("Zero-based step index to mark as completed")] int step_index)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_currentPlan is not null && step_index < _currentPlan.Steps.Count)
                {
                    _currentPlan.Steps[step_index].Status = StepStatus.Completed;
                    PlanCard.RefreshSteps();
                }
            });
            return $"Step {step_index} marked as completed.";
        }

        _session.RegisterTools(
            AIFunctionFactory.Create(create_plan),
            AIFunctionFactory.Create(confirm_plan),
            AIFunctionFactory.Create(update_plan_step));
    }

    private void OnConfirmClicked(object? sender, EventArgs e)
    {
        _session.ProvideResponse("confirm_plan", new PlanConfirmationResult { Confirmed = true });
    }

    private void OnRejectClicked(object? sender, EventArgs e)
    {
        _session.ProvideResponse("confirm_plan", new PlanConfirmationResult { Confirmed = false });
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && _currentPlan is not null)
        {
            PlanCard.IsVisible = width >= 700;
        }
    }
}
