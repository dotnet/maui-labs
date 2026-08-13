using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// AI-specific projected row that preserves the existing block/turn/template binding surface while
/// participating in the provider-neutral chat controls.
/// </summary>
public sealed class ContentContext : ChatContentItem, IDisposable
{
    /// <summary>Creates a standalone context for a block.</summary>
    public ContentContext(
        AgentContext agentContext,
        ContentBlock block)
        : this(
            CreateStandalone(
                agentContext ?? throw new ArgumentNullException(nameof(agentContext)),
                block ?? throw new ArgumentNullException(nameof(block))),
            agentContext,
            owner: null)
    {
    }

    internal ContentContext(
        AgentContext agentContext,
        ContentBlock block,
        MessageListView? owner)
        : this(
            CreateStandalone(
                agentContext ?? throw new ArgumentNullException(nameof(agentContext)),
                block ?? throw new ArgumentNullException(nameof(block))),
            agentContext,
            owner)
    {
    }

    private ContentContext(
        StandaloneContext standalone,
        AgentContext agentContext,
        MessageListView? owner)
        : base(
            standalone.Message,
            standalone.Content,
            conversation: null,
            owner?.Appearance ?? new ChatAppearance())
    {
        AgentContext = agentContext;
        AgentContent = standalone.Content;
        Owner = owner;
        AgentContent.ContentChanged += OnAgentContentChanged;
    }

    internal ContentContext(
        AgentContext agentContext,
        ConversationMessage message,
        AgentBlockContent content,
        AgentChatConversation conversation,
        ChatAppearance appearance,
        MessageListView? owner)
        : base(
            message,
            content,
            conversation,
            appearance)
    {
        AgentContext = agentContext;
        AgentContent = content;
        Owner = owner;
        AgentContent.ContentChanged += OnAgentContentChanged;
    }

    /// <summary>Gets the AI conversation context.</summary>
    public AgentContext AgentContext { get; }

    /// <summary>Gets the underlying renderable block.</summary>
    public ContentBlock Block => AgentContent.Block;

    /// <summary>Gets the containing conversation turn, when persisted.</summary>
    public ConversationTurn? Turn => AgentContent.Turn;

    /// <summary>Gets the stable containing turn identifier.</summary>
    public string? TurnId => Turn?.Id;

    /// <summary>Gets whether this block belongs to the request side of its turn.</summary>
    public bool IsRequest => AgentContent.IsRequest;

    /// <summary>Gets whether this is the first rendered block in its turn.</summary>
    public bool IsFirstInTurn =>
        Turn is not null
        && (Turn.RequestBlocks.FirstOrDefault()
            ?? Turn.ResponseBlocks.FirstOrDefault()) == Block;

    /// <summary>Gets whether this is the last rendered block in its turn.</summary>
    public bool IsLastInTurn =>
        Turn is not null
        && (Turn.ResponseBlocks.LastOrDefault()
            ?? Turn.RequestBlocks.LastOrDefault()) == Block;

    /// <summary>Gets the source block role.</summary>
    public ChatRole? Role => Block.Role;

    /// <summary>Gets whether this block came from the user.</summary>
    public bool IsUser => Block.Role == ChatRole.User;

    /// <summary>Gets whether this block came from the assistant.</summary>
    public bool IsAssistant => Block.Role == ChatRole.Assistant;

    /// <summary>Gets the block lifecycle state.</summary>
    public BlockLifecycleState LifecycleState => Block.LifecycleState;

    /// <summary>Gets the tool name for function, approval, and UI-action blocks.</summary>
    public string? ToolName => Block switch
    {
        FunctionInvocationContentBlock function => function.Call?.Name,
        ToolApprovalBlock approval => approval.ToolName,
        UIActionBlock action => action.ToolName,
        _ => null,
    };

    /// <summary>Gets whether this block is waiting for human input.</summary>
    public bool IsInteractive =>
        Block is IInteractiveBlock and not UIActionBlock;

    /// <summary>Gets rich text content when available.</summary>
    public string? TextContent =>
        Block is RichContentBlock rich ? rich.RawText : null;

    /// <summary>Gets approval status for an approval block.</summary>
    public ApprovalStatus? ApprovalState =>
        Block is ToolApprovalBlock approval ? approval.Status : null;

    /// <summary>Gets whether approval has been resolved.</summary>
    public bool ApprovalResolved =>
        ApprovalState is ApprovalStatus.Approved
            or ApprovalStatus.Rejected;

    /// <summary>Gets user-safe approval resolution text.</summary>
    public string? ApprovalResolutionText => ApprovalState switch
    {
        ApprovalStatus.Approved => $"Approved - {ToolName ?? "Tool"}",
        ApprovalStatus.Rejected => $"Rejected - {ToolName ?? "Tool"}",
        _ => null,
    };

    internal MessageListView? Owner { get; }

    /// <summary>Stops relaying block changes through this projected context.</summary>
    public void Dispose()
    {
        AgentContent.ContentChanged -= OnAgentContentChanged;
    }

    internal void NotifyBlockChanged() =>
        AgentContent.NotifyChanged();

    private void OnAgentContentChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        OnPropertyChanged(string.Empty);
    }

    internal AgentBlockContent AgentContent { get; }

    private static StandaloneContext CreateStandalone(
        AgentContext agentContext,
        ContentBlock block)
    {
        var participant = new ChatParticipant(
            block.Role?.Value ?? "assistant",
            block.AuthorName
                ?? (block.Role == ChatRole.User ? "You" : "Assistant"),
            block.Role == ChatRole.User
                ? ChatParticipantKind.Local
                : ChatParticipantKind.Agent);
        var content = new AgentBlockContent(
            block,
            turn: null,
            isRequest: block.Role == ChatRole.User);
        var message = new ConversationMessage(
            participant,
            string.IsNullOrWhiteSpace(block.Id)
                ? Guid.NewGuid().ToString("N")
                : block.Id,
            block.CreatedAt);
        content.AttachMessage(message);
        message.Contents.Add(content);
        return new StandaloneContext(message, content);
    }

    private sealed record StandaloneContext(
        ConversationMessage Message,
        AgentBlockContent Content);
}
