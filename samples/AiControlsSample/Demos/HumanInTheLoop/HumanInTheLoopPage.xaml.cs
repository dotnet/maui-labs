using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class HumanInTheLoopPage : ContentPage
{
    public AgentContext Session { get; }

    public HumanInTheLoopPage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(CreatePlan, "create_plan", "Create a plan with numbered steps. Returns the plan as formatted text."),
            AIFunctionFactory.Create(ConfirmPlan, "confirm_plan", "Execute the plan after user confirms. Call this ONLY after presenting the plan."),
        };

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a plan assistant. When the user asks you to do something:
                1. First, call create_plan with a list of step descriptions (3-5 steps).
                2. The plan will be shown to the user as a formatted message.
                3. After presenting the plan, ask the user: "Would you like me to proceed with this plan?"
                4. If the user confirms (says yes, proceed, confirm, etc.), call confirm_plan to execute.
                5. If the user rejects or wants changes, acknowledge and ask what they'd like different.
                6. After confirming, describe completing each step one by one with brief updates.

                Always create 3-5 concrete steps. Present them clearly. Wait for user approval.
                """,
            Tools = [.. tools]
        };
        var agent = new UIAgent(chatClient, chatOptions);
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    [Description("Create a plan with numbered steps for the user to review.")]
    private string CreatePlan(
        [Description("JSON array of step descriptions, e.g. [\"Step 1\", \"Step 2\"]")] string steps_json)
    {
        var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];

        var formatted = "📋 **Plan:**\n\n";
        for (int i = 0; i < steps.Count; i++)
        {
            formatted += $"  {i + 1}. {steps[i]}\n";
        }
        formatted += "\n_Waiting for your confirmation to proceed._";

        return formatted;
    }

    [Description("Execute the confirmed plan. Only call after user has approved.")]
    private string ConfirmPlan()
    {
        return "✅ Plan confirmed! Executing steps...";
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
    }
}
