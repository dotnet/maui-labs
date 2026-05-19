using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class HumanInTheLoopPage : ContentPage
{
    public AgentContext Session { get; }

    public HumanInTheLoopPage(IChatClient chatClient)
    {
        // Wrap the chat client to require approval for create_plan.
        // When the LLM calls create_plan, the wrapper emits ToolApprovalRequestContent
        // instead of auto-executing — the pipeline creates a FunctionApprovalBlock
        // and the ToolApprovalView renders Approve/Reject buttons inline.
        var approvalClient = new ToolApprovalChatClient(chatClient, "create_plan");

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
            Tools =
            [
                AIFunctionFactory.Create(
                    [Description("Create a plan with numbered steps for the user to review and approve before execution.")]
                    ([Description("JSON array of step descriptions")] string steps_json) =>
                    {
                        var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];
                        var formatted = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}"));
                        return $"Plan approved and executing:\n{formatted}";
                    },
                    "create_plan")
            ]
        };

        var agent = new UIAgent(approvalClient, chatOptions);
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
    }
}

/// <summary>
/// A delegating chat client that intercepts specific tool calls and emits
/// <see cref="ToolApprovalRequestContent"/> instead of letting them auto-execute.
/// This triggers the FunctionApprovalBlock flow in the pipeline, showing
/// Approve/Reject UI to the user.
/// </summary>
file sealed class ToolApprovalChatClient : DelegatingChatClient
{
    private readonly HashSet<string> _toolsRequiringApproval;

    public ToolApprovalChatClient(IChatClient inner, params string[] toolNames)
        : base(inner)
    {
        _toolsRequiringApproval = new HashSet<string>(toolNames, StringComparer.OrdinalIgnoreCase);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            // Check if any content is a function call for a tool requiring approval
            var hasApprovalCall = false;
            foreach (var content in update.Contents)
            {
                if (content is FunctionCallContent fcc &&
                    _toolsRequiringApproval.Contains(fcc.Name))
                {
                    hasApprovalCall = true;
                    break;
                }
            }

            if (!hasApprovalCall)
            {
                yield return update;
                continue;
            }

            // Replace function calls with approval requests
            var newContents = new List<AIContent>();
            foreach (var content in update.Contents)
            {
                if (content is FunctionCallContent fcc &&
                    _toolsRequiringApproval.Contains(fcc.Name))
                {
                    newContents.Add(new ToolApprovalRequestContent(
                        fcc.CallId ?? Guid.NewGuid().ToString("N"), fcc));
                }
                else
                {
                    newContents.Add(content);
                }
            }

            yield return new ChatResponseUpdate
            {
                Role = update.Role,
                MessageId = update.MessageId,
                Contents = newContents,
                FinishReason = update.FinishReason,
                ResponseId = update.ResponseId,
                ModelId = update.ModelId,
            };
        }
    }
}
