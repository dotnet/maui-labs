using Microsoft.Extensions.AI;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Projects an <see cref="AgentContext"/> into the provider-neutral conversation model used by
/// <see cref="ChatView"/>.
/// </summary>
internal sealed class AgentChatConversation : ChatConversation, IDisposable
{
    private readonly Dictionary<ContentBlock, ConversationMessage> _messagesByBlock =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ContentBlock, AgentBlockContent> _contentsByBlock =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ChatParticipant> _participantsByKey =
        new(StringComparer.Ordinal);
    private readonly IDisposable _blockAdded;
    private readonly IDisposable _statusChanged;
    private readonly IDisposable _responseBlocksCleared;
    private ConversationMessage? _thinkingMessage;
    private ConversationMessage? _errorMessage;
    private bool _disposed;

    /// <summary>Creates a neutral projection over <paramref name="session"/>.</summary>
    public AgentChatConversation(AgentContext session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));

        LocalParticipant = new ChatParticipant(
            "user",
            "You",
            ChatParticipantKind.Local);
        AssistantParticipant = new ChatParticipant(
            "assistant",
            "Assistant",
            ChatParticipantKind.Agent);
        Participants.Add(LocalParticipant);
        Participants.Add(AssistantParticipant);
        _participantsByKey["User:"] = LocalParticipant;
        _participantsByKey["Assistant:"] = AssistantParticipant;

        foreach (var turn in session.Turns)
        {
            foreach (var block in turn.RequestBlocks)
                AddBlock(turn, block, isRequest: true);
            foreach (var block in turn.ResponseBlocks)
                AddBlock(turn, block, isRequest: false);
        }

        _blockAdded = session.RegisterOnBlockAdded(OnBlockAdded);
        _statusChanged = session.RegisterOnStatusChanged(OnStatusChanged);
        _responseBlocksCleared =
            session.RegisterOnResponseBlocksCleared(OnResponseBlocksCleared);
        OnStatusChanged(session.Status);
    }

    /// <summary>Gets the projected agent session.</summary>
    public AgentContext Session { get; }

    /// <summary>Gets the local user participant.</summary>
    public ChatParticipant UserParticipant => LocalParticipant!;

    /// <summary>Gets the default assistant participant.</summary>
    public ChatParticipant AssistantParticipant { get; }

    /// <summary>Gets or sets the display name of the local user.</summary>
    public string UserDisplayName
    {
        get => UserParticipant.DisplayName;
        set => UserParticipant.DisplayName = value;
    }

    /// <summary>Gets or sets the display name used when an assistant update has no author name.</summary>
    public string AssistantDisplayName
    {
        get => AssistantParticipant.DisplayName;
        set => AssistantParticipant.DisplayName = value;
    }

    internal void UpdateParticipantNames(
        string userDisplayName,
        string assistantDisplayName)
    {
        UserDisplayName = userDisplayName;
        AssistantDisplayName = assistantDisplayName;
    }

    /// <inheritdoc />
    public override bool CanSend(ChatDraft? draft) =>
        !_disposed
        && draft is not null
        && !draft.IsEmpty
        && Session.Status is ConversationStatus.Idle or ConversationStatus.Error;

    /// <inheritdoc />
    protected override async Task<bool> SendCoreAsync(
        ChatDraft draft,
        CancellationToken cancellationToken)
    {
        var contents = new List<AIContent>();
        if (draft.HasText)
            contents.Add(new TextContent(draft.Text));

        foreach (var attachment in draft.Attachments)
        {
            AIContent content;
            if (attachment.Uri is { } uri)
            {
                content = new UriContent(uri, attachment.MediaType);
            }
            else
            {
                var dataContent = new DataContent(
                    attachment.Data,
                    attachment.MediaType)
                {
                    Name = attachment.FileName
                };
                content = dataContent;
            }

            contents.Add(content);
        }

        await Session.SendMessageAsync(
            new ChatMessage(ChatRole.User, contents),
            cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _blockAdded.Dispose();
        _statusChanged.Dispose();
        _responseBlocksCleared.Dispose();
        ClearProjection();
    }

    private void OnBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        RemoveThinking();
        AddBlock(
            turn,
            block,
            turn.RequestBlocks.Contains(block));
        UpdateThinking();
    }

    private void OnResponseBlocksCleared(ConversationTurn turn)
    {
        foreach (var pair in _contentsByBlock.ToArray())
        {
            if (ReferenceEquals(pair.Value.Turn, turn)
                && !pair.Value.IsRequest)
            {
                RemoveBlock(pair.Key);
            }
        }

        RemoveThinking();
    }

    private void OnStatusChanged(ConversationStatus status)
    {
        Status = status switch
        {
            ConversationStatus.Streaming => ChatConversationStatus.Busy,
            ConversationStatus.AwaitingInput => ChatConversationStatus.AwaitingInput,
            ConversationStatus.Error => ChatConversationStatus.Error,
            _ => ChatConversationStatus.Idle,
        };

        if (status == ConversationStatus.Streaming)
            RemoveError();

        if (status == ConversationStatus.Error)
        {
            RemoveThinking();
            AddError();
            return;
        }

        if (status == ConversationStatus.Idle)
        {
            RemoveThinking();
            foreach (var message in Messages)
            {
                if (message.Status == ConversationMessageStatus.Sending)
                    message.Status = ConversationMessageStatus.Sent;
            }

            if (Session.Turns.Count == 0)
                ClearProjection();
            return;
        }

        UpdateThinking();
    }

    private void AddBlock(
        ConversationTurn? turn,
        ContentBlock block,
        bool isRequest,
        ChatParticipant? participant = null)
    {
        if (_messagesByBlock.ContainsKey(block))
            return;

        participant ??= GetParticipant(block);
        var content = new AgentBlockContent(block, turn, isRequest);
        var message = new ConversationMessage(
            participant,
            string.IsNullOrWhiteSpace(block.Id)
                ? Guid.NewGuid().ToString("N")
                : block.Id,
            block.CreatedAt)
        {
            Status = block.LifecycleState == BlockLifecycleState.Active
                ? ConversationMessageStatus.Sending
                : ConversationMessageStatus.Sent,
        };
        content.AttachMessage(message);
        message.Contents.Add(content);
        _messagesByBlock.Add(block, message);
        _contentsByBlock.Add(block, content);
        MessageList.Add(message);
    }

    private ChatParticipant GetParticipant(ContentBlock block)
    {
        if (block.Role == ChatRole.User)
            return UserParticipant;

        var role = block.Role?.Value ?? "Assistant";
        var author = block.AuthorName ?? string.Empty;
        if (block.Role == ChatRole.Assistant && author.Length == 0)
            return AssistantParticipant;

        var key = $"{role}:{author}";
        if (_participantsByKey.TryGetValue(key, out var existing))
            return existing;

        var kind = block.Role == ChatRole.Assistant
            ? ChatParticipantKind.Agent
            : ChatParticipantKind.System;
        var participant = new ChatParticipant(
            key,
            author.Length > 0 ? author : role,
            kind);
        _participantsByKey.Add(key, participant);
        Participants.Add(participant);
        return participant;
    }

    private void RemoveBlock(ContentBlock block)
    {
        if (_messagesByBlock.Remove(block, out var message))
            MessageList.Remove(message);
        if (_contentsByBlock.Remove(block, out var content))
            content.Dispose();
    }

    private void UpdateThinking()
    {
        if (!ShouldShowThinking())
        {
            RemoveThinking();
            return;
        }

        if (_thinkingMessage is not null)
            return;

        var block = new ThinkingContentBlock();
        AddBlock(
            turn: null,
            block,
            isRequest: false,
            AssistantParticipant);
        _thinkingMessage = _messagesByBlock[block];
    }

    private bool ShouldShowThinking()
    {
        if (Session.Status != ConversationStatus.Streaming)
            return false;

        var last = Messages
            .SelectMany(message => message.Contents)
            .OfType<AgentBlockContent>()
            .LastOrDefault(content =>
                content.Block is not ThinkingContentBlock
                    and not ErrorContentBlock);
        if (last is null)
            return false;

        return last.Block.Role != ChatRole.Assistant
            || last.Block is not (
                RichContentBlock
                or ReasoningContentBlock
                or MediaContentBlock);
    }

    private void RemoveThinking()
    {
        if (_thinkingMessage is null)
            return;

        var block = ((AgentBlockContent)_thinkingMessage.Contents[0]).Block;
        _thinkingMessage = null;
        RemoveBlock(block);
    }

    private void AddError()
    {
        if (_errorMessage is not null)
            return;

        var block = new ErrorContentBlock(
            ErrorContentBlock.DefaultUserMessage);
        AddBlock(
            turn: null,
            block,
            isRequest: false,
            AssistantParticipant);
        _errorMessage = _messagesByBlock[block];
    }

    private void RemoveError()
    {
        if (_errorMessage is null)
            return;

        var block = ((AgentBlockContent)_errorMessage.Contents[0]).Block;
        _errorMessage = null;
        RemoveBlock(block);
    }

    private void ClearProjection()
    {
        _thinkingMessage = null;
        _errorMessage = null;
        foreach (var content in _contentsByBlock.Values)
            content.Dispose();
        _contentsByBlock.Clear();
        _messagesByBlock.Clear();
        MessageList.Clear();
    }
}

/// <summary>Neutral message content that retains one AI <see cref="ContentBlock"/>.</summary>
internal sealed class AgentBlockContent : MessageContent, IDisposable
{
    private ContentBlockChangedSubscription _subscription;
    private ConversationMessage? _message;
    private bool _disposed;

    internal AgentBlockContent(
        ContentBlock block,
        ConversationTurn? turn,
        bool isRequest)
        : base(block?.Id)
    {
        Block = block ?? throw new ArgumentNullException(nameof(block));
        Turn = turn;
        IsRequest = isRequest;
        _subscription = block.OnChanged(OnBlockChanged);
    }

    /// <summary>Gets the underlying AI block.</summary>
    public ContentBlock Block { get; }

    /// <summary>Gets the containing turn, when this content belongs to persisted history.</summary>
    public ConversationTurn? Turn { get; }

    /// <summary>Gets whether the block is on the request side of its turn.</summary>
    public bool IsRequest { get; }

    internal void AttachMessage(ConversationMessage message) =>
        _message = message;

    internal void NotifyChanged() =>
        OnBlockChanged();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _subscription.Dispose();
    }

    private void OnBlockChanged()
    {
        if (_message is not null)
        {
            _message.Status = Block.LifecycleState == BlockLifecycleState.Active
                ? ConversationMessageStatus.Sending
                : ConversationMessageStatus.Sent;
        }
        RaiseContentChanged();
    }
}
