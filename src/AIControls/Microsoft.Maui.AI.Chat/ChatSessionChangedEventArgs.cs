namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Event payload for <see cref="IChatSession.Changed"/>.
/// </summary>
public sealed class ChatSessionChangedEventArgs : EventArgs
{
    public ChatSessionChangedEventArgs(ChatSessionChangeKind kind, ChatEntry? entry = null, int? index = null)
    {
        Kind = kind;
        Entry = entry;
        Index = index;
    }

    public ChatSessionChangeKind Kind { get; }

    public ChatEntry? Entry { get; }

    public int? Index { get; }
}
