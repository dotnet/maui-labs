using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Controls.Chat;

/// <summary>
/// Thin MAUI wrapper around a headless <see cref="ChatEntry"/>.
/// </summary>
public sealed class ContentContext
{
    public ContentContext(IChatSession session, ChatEntry entry)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public IChatSession Session { get; }

    public ChatEntry Entry { get; }

    public AIContent Content => Entry.Content;

    public ContentRole Role => Entry.Role;

    public string? ToolName => Entry.ToolName;

    public ToolApprovalState ApprovalState => Entry.ApprovalState;

    public bool ApprovalResolved =>
        Entry.ApprovalState is ToolApprovalState.Approved or ToolApprovalState.Rejected;

    public string? ApprovalResolutionText => Entry.ApprovalState switch
    {
        ToolApprovalState.Approved => $"Approved - {ToolName ?? "Tool"}",
        ToolApprovalState.Rejected => $"Rejected - {ToolName ?? "Tool"}",
        _ => null,
    };
}
