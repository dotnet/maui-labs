using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AiControlsSample;

public partial class AgenticGenerativeUIPage : ContentPage
{
    private readonly IAgentSession _session;
    private Plan? _currentPlan;

    public AgenticGenerativeUIPage(IAgentSessionFactory sessionFactory, IChatClient chatClient)
    {
        InitializeComponent();

        _session = sessionFactory.Create(chatClient);
        _session.SystemInstructions = """
            You are an auto-planner. When the user asks you to do something:
            1. Create a plan by calling create_plan with step descriptions.
            2. Immediately start executing each step, calling complete_step for each.
            3. Do NOT wait for user confirmation — just execute.
            4. After all steps are done, summarize what you accomplished.
            
            Create 3-5 concrete steps. Execute them one by one, waiting briefly between each.
            """;

        RegisterTools();

        ChatView.Session = _session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Go to Mars", "Build a plan to go to Mars in 5 steps"),
            new Suggestion("Make pizza", "Build a plan to make pizza from scratch"),
            new Suggestion("Learn guitar", "Build a plan to learn guitar in a month"),
        ];
    }

    private void RegisterTools()
    {
        [Description("Create a plan with the given steps. Returns the plan ID.")]
        string create_plan(
            [Description("JSON array of step descriptions")] string steps_json)
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
            });
            return $"Plan created with {plan.Steps.Count} steps.";
        }

        [Description("Mark a plan step as completed by zero-based index.")]
        string complete_step(
            [Description("Zero-based step index to complete")] int step_index)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_currentPlan is not null && step_index >= 0 && step_index < _currentPlan.Steps.Count)
                {
                    _currentPlan.Steps[step_index].Status = StepStatus.Completed;
                    PlanCard.RefreshSteps();
                }
            });
            return $"Step {step_index} completed.";
        }

        _session.RegisterTools(
            AIFunctionFactory.Create(create_plan),
            AIFunctionFactory.Create(complete_step));
    }
}
