using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

public sealed class TeamChatViewModel : INotifyPropertyChanged
{
    private readonly ChatParticipant _morgan = new(
        "morgan",
        "Morgan",
        ChatParticipantKind.Local);
    private readonly ChatParticipant _priya = new(
        "priya",
        "Priya",
        ChatParticipantKind.Remote);
    private readonly ChatParticipant _diego = new(
        "diego",
        "Diego",
        ChatParticipantKind.Remote);
    private ChatParticipant _selectedParticipant;
    private bool _priyaIsTyping;
    private bool _diegoIsTyping;
    private bool _isConversationBusy;
    private bool _failNextSend;
    private bool _slowNextSend;
    private ConversationMessageStatus _selectedDeliveryStatus =
        ConversationMessageStatus.Delivered;

    public TeamChatViewModel()
    {
        Participants = [_morgan, _priya, _diego];
        _selectedParticipant = _priya;

        Conversation = new ObservableChatConversation(_morgan)
        {
            SendHandler = SendAsync,
        };
        Conversation.Participants.Add(_priya);
        Conversation.Participants.Add(_diego);

        Suggestions =
        [
            new ChatSuggestion(
                "Watering plan",
                "Who can water the herb bed tomorrow?",
                "\U0001F4A7"),
            new ChatSuggestion(
                "Share a task",
                "Please share the seed-order task.",
                "\u2705"),
            new ChatSuggestion(
                "Weekend update",
                "What changed in the garden this weekend?",
                "\U0001F331"),
        ];
        DeliveryStatuses = Enum.GetValues<ConversationMessageStatus>();

        ResetCommand = new Command(Reset);
        ClearCommand = new Command(Clear);
        CycleThemeCommand = new Command(CycleTheme);
        SendParticipantTextCommand = new Command(SendParticipantText);
        SendParticipantPhotoCommand = new Command(SendParticipantPhoto);
        SendParticipantFileCommand = new Command(SendParticipantFile);
        SendParticipantTaskCommand = new Command(SendParticipantTask);
        SendParticipantMultipartCommand = new Command(SendParticipantMultipart);
        StreamParticipantTextCommand = new Command(() => _ = StreamParticipantTextAsync());
        SendParticipantBurstCommand = new Command(SendParticipantBurst);
        SendParticipantStickerCommand = new Command(SendParticipantSticker);
        ApplyDeliveryStatusCommand = new Command(ApplyDeliveryStatus);
        EditLastTextCommand = new Command(EditLastText);
        RemoveLastMessageCommand = new Command(RemoveLastMessage);

        Reset();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableChatConversation Conversation { get; }

    public IReadOnlyList<ChatParticipant> Participants { get; }

    public ObservableCollection<ChatSuggestion> Suggestions { get; }

    public IReadOnlyList<ConversationMessageStatus> DeliveryStatuses { get; }

    public ICommand ResetCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand CycleThemeCommand { get; }

    public ICommand SendParticipantTextCommand { get; }

    public ICommand SendParticipantPhotoCommand { get; }

    public ICommand SendParticipantFileCommand { get; }

    public ICommand SendParticipantTaskCommand { get; }

    public ICommand SendParticipantMultipartCommand { get; }

    public ICommand StreamParticipantTextCommand { get; }

    public ICommand SendParticipantBurstCommand { get; }

    public ICommand SendParticipantStickerCommand { get; }

    public ICommand ApplyDeliveryStatusCommand { get; }

    public ICommand EditLastTextCommand { get; }

    public ICommand RemoveLastMessageCommand { get; }

    public ChatParticipant SelectedParticipant
    {
        get => _selectedParticipant;
        set => SetProperty(ref _selectedParticipant, value);
    }

    public bool PriyaIsTyping
    {
        get => _priyaIsTyping;
        set
        {
            if (SetProperty(ref _priyaIsTyping, value))
                SetTyping(_priya, value);
        }
    }

    public bool DiegoIsTyping
    {
        get => _diegoIsTyping;
        set
        {
            if (SetProperty(ref _diegoIsTyping, value))
                SetTyping(_diego, value);
        }
    }

    public bool IsConversationBusy
    {
        get => _isConversationBusy;
        set
        {
            if (SetProperty(ref _isConversationBusy, value))
            {
                Conversation.SetStatus(
                    value
                        ? ChatConversationStatus.Busy
                        : ChatConversationStatus.Idle);
            }
        }
    }

    public bool FailNextSend
    {
        get => _failNextSend;
        set => SetProperty(ref _failNextSend, value);
    }

    public bool SlowNextSend
    {
        get => _slowNextSend;
        set => SetProperty(ref _slowNextSend, value);
    }

    public ConversationMessageStatus SelectedDeliveryStatus
    {
        get => _selectedDeliveryStatus;
        set => SetProperty(ref _selectedDeliveryStatus, value);
    }

    public string LastOutgoingStatusText
    {
        get
        {
            var message = GetLastOutgoingMessage();
            return message is null
                ? "No outgoing message"
                : $"Last outgoing: {message.Status}";
        }
    }

    public string ThemeButtonText => Application.Current?.UserAppTheme switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        _ => "System",
    };

    private void Reset()
    {
        Clear();
        var now = DateTimeOffset.Now;

        Conversation.AddMessage(new ConversationMessage(
            _priya,
            "Morning! The community garden beds are ready for our spring layout.",
            id: "welcome",
            createdAt: now.AddMinutes(-18))
        {
            Status = ConversationMessageStatus.Read,
        });

        var photo = CreateMessage(
            _diego,
            "photo",
            now.AddMinutes(-14));
        photo.AddContent(new TextMessageContent(
            "I found a playful mascot idea for the shared herb bed. What do you think?"));
        photo.AddContent(CreatePhoto());
        Conversation.AddMessage(photo);

        var task = CreateMessage(
            _priya,
            "task",
            now.AddMinutes(-9));
        task.AddContent(new GardenTaskContent(
            "Confirm seed order",
            "Morgan",
            "Today, 4:00 PM",
            GardenTaskPriority.High));
        Conversation.AddMessage(task);

        var file = CreateMessage(
            _diego,
            "file",
            now.AddMinutes(-7));
        file.AddContent(CreateFile());
        Conversation.AddMessage(file);

        Conversation.AddMessage(new ConversationMessage(
            _morgan,
            "Looks great. I will confirm the seeds after lunch.",
            id: "reply",
            createdAt: now.AddMinutes(-5))
        {
            Status = ConversationMessageStatus.Delivered,
        });
        NotifyLastOutgoingStatusChanged();
    }

    private void Clear()
    {
        _priyaIsTyping = false;
        _diegoIsTyping = false;
        _isConversationBusy = false;
        _failNextSend = false;
        _slowNextSend = false;
        OnPropertyChanged(nameof(PriyaIsTyping));
        OnPropertyChanged(nameof(DiegoIsTyping));
        OnPropertyChanged(nameof(IsConversationBusy));
        OnPropertyChanged(nameof(FailNextSend));
        OnPropertyChanged(nameof(SlowNextSend));
        Conversation.Reset();
        NotifyLastOutgoingStatusChanged();
    }

    private async Task<bool> SendAsync(
        ObservableChatConversation conversation,
        ChatDraft draft,
        CancellationToken cancellationToken)
    {
        if (FailNextSend)
        {
            FailNextSend = false;
            throw new InvalidOperationException("Simulated transport failure.");
        }

        conversation.SetStatus(ChatConversationStatus.Busy);
        var outgoing = new ConversationMessage(_morgan)
        {
            Status = ConversationMessageStatus.Sending,
        };
        foreach (var content in draft.CreateContents())
            outgoing.Contents.Add(content);
        conversation.AddMessage(outgoing);
        NotifyLastOutgoingStatusChanged();

        try
        {
            var deliveryDelay = SlowNextSend
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromMilliseconds(250);
            SlowNextSend = false;
            await Task.Delay(deliveryDelay, cancellationToken);
            outgoing.Status = ConversationMessageStatus.Sent;
            NotifyLastOutgoingStatusChanged();
            await Task.Delay(250, cancellationToken);
            outgoing.Status = ConversationMessageStatus.Delivered;
            NotifyLastOutgoingStatusChanged();
            return true;
        }
        catch (OperationCanceledException)
        {
            conversation.RemoveMessage(outgoing);
            NotifyLastOutgoingStatusChanged();
            return false;
        }
        finally
        {
            conversation.SetStatus(ChatConversationStatus.Idle);
        }
    }

    private void SendParticipantText()
    {
        var text = SelectedParticipant == _morgan
            ? "I can take the morning watering shift."
            : SelectedParticipant == _priya
                ? "I updated the planting calendar and marked the seed order as ready."
                : "The west bed is watered. I also checked the tomato supports.";

        AddParticipantMessage(new TextMessageContent(text));
    }

    private void SendParticipantPhoto()
    {
        var message = CreateSelectedMessage();
        message.AddContent(new TextMessageContent("Here is the latest garden photo."));
        message.AddContent(CreatePhoto());
        Conversation.AddMessage(message);
        NotifyLastOutgoingStatusChanged();
    }

    private void SendParticipantFile() =>
        AddParticipantMessage(CreateFile());

    private void SendParticipantTask() =>
        AddParticipantMessage(new GardenTaskContent(
            "Prepare the north bed",
            SelectedParticipant.DisplayName,
            "Friday, 3:30 PM",
            GardenTaskPriority.High));

    private void SendParticipantMultipart()
    {
        var message = CreateSelectedMessage();
        message.AddContent(new TextMessageContent(
            "Everything for the next work session is in one message."));
        message.AddContent(CreatePhoto());
        message.AddContent(CreateFile());
        message.AddContent(new GardenTaskContent(
            "Bring compost to the east gate",
            SelectedParticipant.DisplayName,
            "Saturday, 8:00 AM"));
        Conversation.AddMessage(message);
        NotifyLastOutgoingStatusChanged();
    }

    private async Task StreamParticipantTextAsync()
    {
        var content = new TextMessageContent("Streaming");
        var message = CreateSelectedMessage();
        message.AddContent(content);
        Conversation.AddMessage(message);
        NotifyLastOutgoingStatusChanged();

        foreach (var chunk in new[] { " content", " updates", " in place", "." })
        {
            await Task.Delay(250);
            content.Append(chunk);
        }
    }

    private void SendParticipantBurst()
    {
        Conversation.AddMessage(CreateTextMessage(
            SelectedParticipant,
            "First message in a participant group."));
        Conversation.AddMessage(CreateTextMessage(
            SelectedParticipant,
            "Second message should share the same grouping chrome."));
        var other = SelectedParticipant == _diego ? _priya : _diego;
        Conversation.AddMessage(CreateTextMessage(
            other,
            "A different participant starts a new group."));
        NotifyLastOutgoingStatusChanged();
    }

    private void SendParticipantSticker() =>
        AddParticipantMessage(new GardenStickerContent(
            "\U0001F33B",
            "A sunflower sticker"));

    private void ApplyDeliveryStatus()
    {
        if (GetLastOutgoingMessage() is { } message)
            message.Status = SelectedDeliveryStatus;
        NotifyLastOutgoingStatusChanged();
    }

    private void EditLastText()
    {
        var content = Conversation.Messages
            .Reverse()
            .SelectMany(message => message.Contents.Reverse())
            .OfType<TextMessageContent>()
            .FirstOrDefault();
        content?.Append(" (edited live)");
    }

    private void RemoveLastMessage()
    {
        if (Conversation.Messages.LastOrDefault() is { } message)
            Conversation.RemoveMessage(message);
        NotifyLastOutgoingStatusChanged();
    }

    private void AddParticipantMessage(MessageContent content)
    {
        var message = CreateSelectedMessage();
        message.AddContent(content);
        Conversation.AddMessage(message);
        NotifyLastOutgoingStatusChanged();
    }

    private ConversationMessage CreateSelectedMessage() =>
        CreateMessage(
            SelectedParticipant,
            id: null,
            DateTimeOffset.Now);

    private ConversationMessage CreateTextMessage(
        ChatParticipant participant,
        string text)
    {
        var message = CreateMessage(
            participant,
            id: null,
            DateTimeOffset.Now);
        message.AddContent(new TextMessageContent(text));
        return message;
    }

    private ConversationMessage CreateMessage(
        ChatParticipant participant,
        string? id,
        DateTimeOffset createdAt) =>
        new(participant, id: id, createdAt: createdAt)
        {
            Status = participant.Kind == ChatParticipantKind.Local
                ? ConversationMessageStatus.Sent
                : ConversationMessageStatus.Read,
        };

    private static MediaMessageContent CreatePhoto() =>
        new(
            new Uri("dotnet_bot.png", UriKind.Relative),
            "image/png")
        {
            FileName = "garden-photo.png",
            AltText = "A purple .NET bot garden mascot",
        };

    private static MediaMessageContent CreateFile() =>
        new(
            new Uri("https://example.invalid/garden-layout.pdf"),
            "application/pdf")
        {
            FileName = "spring-layout.pdf",
            AltText = "Spring garden layout document",
        };

    private void SetTyping(
        ChatParticipant participant,
        bool isTyping)
    {
        if (isTyping)
        {
            if (!Conversation.TypingParticipants.Contains(participant))
                Conversation.TypingParticipants.Add(participant);
        }
        else
        {
            Conversation.TypingParticipants.Remove(participant);
        }
    }

    private ConversationMessage? GetLastOutgoingMessage() =>
        Conversation.Messages.LastOrDefault(
            message => message.Participant == _morgan);

    private void NotifyLastOutgoingStatusChanged() =>
        OnPropertyChanged(nameof(LastOutgoingStatusText));

    private void CycleTheme()
    {
        if (Application.Current is not { } app)
            return;

        app.UserAppTheme = app.UserAppTheme switch
        {
            AppTheme.Unspecified => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
        OnPropertyChanged(nameof(ThemeButtonText));
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
