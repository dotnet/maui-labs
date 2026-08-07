using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Thin MAUI wrapper around a <see cref="ContentBlock"/> from the Core engine, exposing UI-friendly
/// helpers (role, tool name, approval state) for templates and views to bind against.
/// </summary>
/// <remarks>
/// One <see cref="ContentContext"/> is created per block by <see cref="CopilotChatView"/>; a
/// <see cref="ContentTemplateSelector"/> then picks the <see cref="ContentTemplate"/> that renders it.
/// </remarks>
public sealed class ContentContext
{
    public ContentContext(AgentContext agentContext, ContentBlock block)
        : this(agentContext, block, owner: null)
    {
    }

    internal ContentContext(AgentContext agentContext, ContentBlock block, MessageListView? owner)
        : this(agentContext, block, owner, turn: null)
    {
    }

    internal ContentContext(
        AgentContext agentContext,
        ContentBlock block,
        MessageListView? owner,
        ConversationTurn? turn)
    {
        AgentContext = agentContext ?? throw new ArgumentNullException(nameof(agentContext));
        Block = block ?? throw new ArgumentNullException(nameof(block));
        Owner = owner;
        Turn = turn;
    }

    public AgentContext AgentContext { get; }

    public ContentBlock Block { get; }

    /// <summary>Gets the conversation turn containing this block, when it is part of persisted history.</summary>
    public ConversationTurn? Turn { get; }

    /// <summary>Gets the stable turn identifier, when available.</summary>
    public string? TurnId => Turn?.Id;

    /// <summary>Gets whether this block belongs to the request side of its turn.</summary>
    public bool IsRequest => Turn?.RequestBlocks.Contains(Block) == true;

    /// <summary>Gets whether this is the first block rendered for its turn.</summary>
    public bool IsFirstInTurn =>
        Turn is not null
        && (Turn.RequestBlocks.FirstOrDefault()
            ?? Turn.ResponseBlocks.FirstOrDefault()) == Block;

    /// <summary>Gets whether this is the last block currently rendered for its turn.</summary>
    public bool IsLastInTurn =>
        Turn is not null
        && (Turn.ResponseBlocks.LastOrDefault()
            ?? Turn.RequestBlocks.LastOrDefault()) == Block;

    internal MessageListView? Owner { get; }

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
        ToolApprovalBlock fab => fab.ToolName,
        UIActionBlock action => action.ToolName,
        _ => null,
    };

    /// <summary>Whether this block is awaiting human input.</summary>
    public bool IsInteractive => Block is IInteractiveBlock and not UIActionBlock;

    /// <summary>Gets the text content if this is a <see cref="RichContentBlock"/>.</summary>
    public string? TextContent => Block is RichContentBlock rich ? rich.RawText : null;

    /// <summary>Approval status for ToolApprovalBlock, null otherwise.</summary>
    public ApprovalStatus? ApprovalState => Block is ToolApprovalBlock fab ? fab.Status : null;

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
