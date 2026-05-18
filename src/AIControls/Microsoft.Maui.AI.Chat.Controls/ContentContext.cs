using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Thin MAUI wrapper around a <see cref="ContentBlock"/> from the Core engine.
/// </summary>
public sealed class ContentContext
{
    public ContentContext(AgentContext agentContext, ContentBlock block)
    {
        AgentContext = agentContext ?? throw new ArgumentNullException(nameof(agentContext));
        Block = block ?? throw new ArgumentNullException(nameof(block));
    }

    public AgentContext AgentContext { get; }

    public ContentBlock Block { get; }

    /// <summary>The role of this block (User, Assistant, Tool).</summary>
    public ChatRole? Role => Block.Role;

    /// <summary>True if this is a user message.</summary>
    public bool IsUser => Block.Role == ChatRole.User;

    /// <summary>True if this is an assistant message.</summary>
    public bool IsAssistant => Block.Role == ChatRole.Assistant;

    /// <summary>The block lifecycle state (Pending, Active, Inactive).</summary>
    public BlockLifecycleState LifecycleState => Block.LifecycleState;

    /// <summary>Tool name for function invocation/approval blocks.</summary>
    public string? ToolName => Block switch
    {
        FunctionInvocationContentBlock ficb => ficb.Call?.Name,
        FunctionApprovalBlock fab => fab.ToolName,
        _ => null,
    };

    /// <summary>Whether this block is an interactive block awaiting user input.</summary>
    public bool IsInteractive => Block is IInteractiveBlock;

    /// <summary>Gets the text content if this is a RichContentBlock.</summary>
    public string? TextContent => Block is RichContentBlock rcb ? rcb.RawText : null;

    /// <summary>Approval status for FunctionApprovalBlock, null otherwise.</summary>
    public ApprovalStatus? ApprovalState => Block is FunctionApprovalBlock fab ? fab.Status : null;

    /// <summary>Whether approval has been resolved (approved or rejected).</summary>
    public bool ApprovalResolved =>
        ApprovalState is ApprovalStatus.Approved or ApprovalStatus.Rejected;

    /// <summary>Resolution text for resolved approval blocks.</summary>
    public string? ApprovalResolutionText => ApprovalState switch
    {
        ApprovalStatus.Approved => $"Approved - {ToolName ?? "Tool"}",
        ApprovalStatus.Rejected => $"Rejected - {ToolName ?? "Tool"}",
        _ => null,
    };
}
