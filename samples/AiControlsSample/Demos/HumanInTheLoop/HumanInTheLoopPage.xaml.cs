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
        // Wrap the create_plan tool in ApprovalRequiredAIFunction.
        // FunctionInvokingChatClient recognizes this wrapper and emits
        // ToolApprovalRequestContent instead of auto-executing — the pipeline
        // creates a FunctionApprovalBlock and the ToolApprovalView renders
        // Approve/Reject buttons inline.
        var createPlanTool = new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create(
                [Description("Create a plan with numbered steps for the user to review and approve before execution.")]
                ([Description("JSON array of step descriptions")] string steps_json) =>
                {
                    var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];
                    var formatted = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}"));
                    return $"Plan approved and executing:\n{formatted}";
                },
                "create_plan"));

        var agent = new UIAgent(chatClient, options =>
        {
            options.ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are a plan assistant. When the user asks you to do something:
                    1. Create a plan by calling create_plan with a JSON array of step descriptions.
                    2. The plan will be shown to the user for approval — WAIT for them to approve or reject.
                    3. If approved, summarize what was accomplished.
                    4. If rejected or the tool call was not approved, do NOT repeat or describe the plan.
                       Simply say "No problem! What would you like me to change?" and wait for new instructions.

                    Always create 3-5 concrete, actionable steps.
                    """,
                Tools = [createPlanTool]
            };
        });
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
    }
}
