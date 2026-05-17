namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Describes the kind of state change raised by <see cref="IChatSession.Changed"/>.
/// </summary>
public enum ChatSessionChangeKind
{
    Reset,
    MessageAdded,
    MessageUpdated,
    StateChanged,
}
