using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Immutable transcript item emitted by a <see cref="ChatSession"/>.
/// </summary>
public sealed record ChatEntry(
    string Id,
    AIContent Content,
    ContentRole Role,
    DateTimeOffset Timestamp,
    string? ToolName = null,
    ToolApprovalState ApprovalState = ToolApprovalState.None);
