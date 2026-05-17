namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Tracks the lifecycle of a tool approval request.
/// </summary>
public enum ToolApprovalState
{
    None,
    Pending,
    Approved,
    Rejected,
}
