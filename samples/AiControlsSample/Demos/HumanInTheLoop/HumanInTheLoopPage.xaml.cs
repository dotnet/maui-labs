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
        // create_plan is a UIAction — the conversation pauses (AwaitingInput)
        // and the framework renders a ToolApprovalView inline in the chat.
        // The user sees the plan arguments and clicks Approve or Reject.
        var createPlan = AIFunctionFactory.Create(
            ([Description("JSON array of step descriptions")] string steps_json) =>
            {
                var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];
                var formatted = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}"));
                return $"Plan approved. Steps:\n{formatted}";
            },
            "create_plan",
            "Create a plan with numbered steps for the user to review and approve before execution.");

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a plan assistant. When the user asks you to do something:
                1. Create a plan by calling create_plan with a JSON array of step descriptions.
                2. The plan will be shown to the user for approval — WAIT for them to approve or reject.
                3. If approved, describe executing each step one by one with brief updates.
                4. If rejected, acknowledge and ask what they'd like to change.

                Always create 3-5 concrete, actionable steps.
                """,
        };

        var agent = new UIAgent(chatClient, options =>
        {
            options.ChatOptions = chatOptions;
            options.RegisterUIAction(createPlan);
        });
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
    }
}
