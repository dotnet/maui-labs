using Microsoft.Maui.Chat.Controls;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// A provider-neutral group conversation used to exercise the reusable chat control without any AI
/// engine, model, or provider types.
/// </summary>
public sealed class TeamChatViewModel
{
    private readonly ChatParticipant _morgan;
    private readonly ChatParticipant _priya;
    private readonly ChatParticipant _diego;

    public TeamChatViewModel()
    {
        _morgan = new ChatParticipant(
            "morgan",
            "Morgan",
            ChatParticipantKind.Local);
        _priya = new ChatParticipant(
            "priya",
            "Priya",
            ChatParticipantKind.Remote);
        _diego = new ChatParticipant(
            "diego",
            "Diego",
            ChatParticipantKind.Remote);

        Conversation = new ObservableChatConversation(_morgan)
        {
            SendHandler = SendAsync,
        };
        Conversation.Participants.Add(_priya);
        Conversation.Participants.Add(_diego);

        SeedConversation();
    }

    public ObservableChatConversation Conversation { get; }

    private void SeedConversation()
    {
        var now = DateTimeOffset.Now;
        Conversation.AddMessage(new ConversationMessage(
            _priya,
            "Morning! The community garden beds are ready for our spring layout.",
            id: "welcome",
            createdAt: now.AddMinutes(-18))
        {
            Status = ConversationMessageStatus.Read,
        });

        var photo = new ConversationMessage(
            _diego,
            id: "photo",
            createdAt: now.AddMinutes(-14))
        {
            Status = ConversationMessageStatus.Read,
        };
        photo.AddContent(new TextMessageContent(
            "I found a playful mascot idea for the shared herb bed. What do you think?"));
        photo.AddContent(new MediaMessageContent(
            new Uri("dotnet_bot.png", UriKind.Relative),
            "image/png")
        {
            FileName = "herb-bed-marker.png",
            AltText = "A purple .NET bot spaceship mascot",
        });
        Conversation.AddMessage(photo);

        var task = new ConversationMessage(
            _priya,
            id: "task",
            createdAt: now.AddMinutes(-9))
        {
            Status = ConversationMessageStatus.Read,
        };
        task.AddContent(new GardenTaskContent(
            "Confirm seed order",
            "Morgan",
            "Today, 4:00 PM"));
        Conversation.AddMessage(task);

        Conversation.AddMessage(new ConversationMessage(
            _morgan,
            "Looks great. I will confirm the seeds after lunch.",
            id: "reply",
            createdAt: now.AddMinutes(-5))
        {
            Status = ConversationMessageStatus.Delivered,
        });
    }

    private async Task<bool> SendAsync(
        ObservableChatConversation conversation,
        ChatDraft draft,
        CancellationToken cancellationToken)
    {
        conversation.SetStatus(ChatConversationStatus.Busy);
        try
        {
            var outgoing = new ConversationMessage(_morgan)
            {
                Status = ConversationMessageStatus.Sending,
            };
            foreach (var content in draft.CreateContents())
                outgoing.Contents.Add(content);
            conversation.AddMessage(outgoing);

            await Task.Delay(250, cancellationToken);
            outgoing.Status = ConversationMessageStatus.Sent;
            await Task.Delay(250, cancellationToken);
            outgoing.Status = ConversationMessageStatus.Delivered;

            conversation.TypingParticipants.Add(_priya);
            await Task.Delay(450, cancellationToken);
            conversation.TypingParticipants.Remove(_priya);
            conversation.AddMessage(new ConversationMessage(
                _priya,
                "Thanks! I have added that to the crew notes.")
            {
                Status = ConversationMessageStatus.Read,
            });

            return true;
        }
        finally
        {
            conversation.TypingParticipants.Remove(_priya);
            conversation.SetStatus(ChatConversationStatus.Idle);
        }
    }
}
