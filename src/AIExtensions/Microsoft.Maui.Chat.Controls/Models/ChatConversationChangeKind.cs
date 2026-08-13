namespace Microsoft.Maui.Chat.Controls;

/// <summary>What a <see cref="ChatConversationChange"/> describes.</summary>
public enum ChatConversationChangeKind
{
    /// <summary>Everything changed; subscribers must re-read the conversation from scratch.</summary>
    Reset,

    /// <summary>A message was inserted at <see cref="ChatConversationChange.Index"/>.</summary>
    MessageAdded,

    /// <summary>A message was removed from <see cref="ChatConversationChange.Index"/>.</summary>
    MessageRemoved,

    /// <summary>A message's own state changed (status, error text, or its content list was reset).</summary>
    MessageChanged,

    /// <summary>Content was inserted into a message at <see cref="ChatConversationChange.Index"/>.</summary>
    ContentAdded,

    /// <summary>Content was removed from a message at <see cref="ChatConversationChange.Index"/>.</summary>
    ContentRemoved,

    /// <summary>Existing content changed in place, for example streamed text was appended.</summary>
    ContentChanged,

    /// <summary>The conversation's <see cref="ChatConversation.Status"/> changed.</summary>
    StatusChanged,
}
