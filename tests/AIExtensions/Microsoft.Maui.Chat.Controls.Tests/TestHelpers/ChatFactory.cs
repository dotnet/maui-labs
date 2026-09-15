namespace Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

/// <summary>Builds the small object graphs the tests need, so each test only states what matters to it.</summary>
internal static class ChatFactory
{
    public static ChatParticipant Local(string id = "me", string? name = null) =>
        new(id, name ?? "Me", ChatParticipantKind.Local);

    public static ChatParticipant Remote(string id = "them", string? name = null) =>
        new(id, name ?? "Them", ChatParticipantKind.Remote);

    public static ChatParticipant Agent(string id = "bot", string? name = null) =>
        new(id, name ?? "Assistant", ChatParticipantKind.Agent);

    /// <summary>Creates a conversation with a local participant and, optionally, a remote one.</summary>
    public static ObservableChatConversation Conversation(
        out ChatParticipant local,
        out ChatParticipant remote)
    {
        local = Local();
        remote = Remote();

        var conversation = new ObservableChatConversation(local);
        conversation.Participants.Add(remote);
        return conversation;
    }

    public static ObservableChatConversation Conversation() => Conversation(out _, out _);

    public static ConversationMessage Text(ChatParticipant participant, string text) =>
        new(participant, text);

    public static MediaMessageContent Image(string fileName = "photo.png") =>
        new(new byte[] { 1, 2, 3, 4 }, "image/png") { FileName = fileName };

    public static MediaMessageContent File(string fileName = "notes.pdf") =>
        new(new byte[] { 1, 2, 3, 4, 5 }, "application/pdf") { FileName = fileName };

    public static ChatAttachment Attachment(string fileName = "photo.png", string mediaType = "image/png") =>
        new(fileName, mediaType, new byte[] { 1, 2, 3 });

    public static ChatContentItem Item(
        ChatParticipant participant,
        MessageContent? content = null,
        ChatConversation? conversation = null,
        ChatAppearance? appearance = null)
    {
        var message = new ConversationMessage(participant);
        var actual = content ?? new TextMessageContent("hello");
        message.Contents.Add(actual);

        return new ChatContentItem(message, actual, conversation, appearance);
    }
}
