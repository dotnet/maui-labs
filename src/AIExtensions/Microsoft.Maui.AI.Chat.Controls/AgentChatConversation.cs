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
    private readonly Dictionary<ContentBlock, List<IAgentBlockContent>> _contentsByBlock =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ContentBlock, BlockProjectionMetadata> _metadataByBlock =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ContentBlock, ContentBlockChangedSubscription> _mediaSyncSubscriptions =
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
    /// <remarks>
    /// <see cref="ConversationStatus.AwaitingInput"/> is resolved by the active interactive block
    /// (for example an approval), so the free-form composer intentionally remains disabled.
    /// </remarks>
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
        foreach (var pair in _metadataByBlock.ToArray())
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
        var contents = CreateMessageContents(block, turn, isRequest);
        var agentContents = contents
            .Cast<IAgentBlockContent>()
            .ToList();
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
        foreach (var content in agentContents)
        {
            content.AttachMessage(message);
            message.Contents.Add((MessageContent)content);
        }
        _messagesByBlock.Add(block, message);
        _contentsByBlock.Add(block, agentContents);
        _metadataByBlock.Add(
            block,
            new BlockProjectionMetadata(turn, isRequest));
        if (block is MediaContentBlock)
        {
            _mediaSyncSubscriptions.Add(
                block,
                block.OnChanged(() => OnMediaBlockChanged(block)));
        }
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
        if (_contentsByBlock.Remove(block, out var contents))
        {
            foreach (var content in contents)
                content.Dispose();
        }
        if (_mediaSyncSubscriptions.Remove(block, out var subscription))
            subscription.Dispose();
        _metadataByBlock.Remove(block);
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
            .OfType<IAgentBlockContent>()
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

        var block = ((IAgentBlockContent)_thinkingMessage.Contents[0]).Block;
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

        var block = ((IAgentBlockContent)_errorMessage.Contents[0]).Block;
        _errorMessage = null;
        RemoveBlock(block);
    }

    private void ClearProjection()
    {
        _thinkingMessage = null;
        _errorMessage = null;
        foreach (var contents in _contentsByBlock.Values)
        {
            foreach (var content in contents)
                content.Dispose();
        }
        foreach (var subscription in _mediaSyncSubscriptions.Values)
            subscription.Dispose();
        _mediaSyncSubscriptions.Clear();
        _metadataByBlock.Clear();
        _contentsByBlock.Clear();
        _messagesByBlock.Clear();
        MessageList.Clear();
    }

    internal static MessageContent CreateMessageContent(
        ContentBlock block,
        ConversationTurn? turn,
        bool isRequest)
    {
        var contents = CreateMessageContents(block, turn, isRequest);
        return contents.Count > 0
            ? contents[0]
            : new AgentBlockContent(block, turn, isRequest);
    }

    internal static IReadOnlyList<MessageContent> CreateMessageContents(
        ContentBlock block,
        ConversationTurn? turn,
        bool isRequest) =>
        block switch
        {
            TextContentBlock text =>
                [new AgentTextMessageContent(text, turn, isRequest)],
            RichContentBlock rich =>
                [new AgentStructuredTextMessageContent(rich, turn, isRequest)],
            MediaContentBlock { Items.Count: > 0 } media =>
                media.Items
                    .Select((item, index) => (MessageContent)new AgentMediaMessageContent(
                        media,
                        item,
                        index,
                        turn,
                        isRequest))
                    .ToArray(),
            MediaContentBlock => [],
            _ => [new AgentBlockContent(block, turn, isRequest)],
        };

    private void OnMediaBlockChanged(ContentBlock block)
    {
        SynchronizeMediaContent(block);
        if (!_contentsByBlock.TryGetValue(block, out var contents))
            return;

        foreach (var content in contents)
            content.NotifyChanged();
    }

    private void SynchronizeMediaContent(ContentBlock block)
    {
        if (block is not MediaContentBlock media ||
            !_messagesByBlock.TryGetValue(block, out var message) ||
            !_contentsByBlock.TryGetValue(block, out var contents) ||
            !_metadataByBlock.TryGetValue(block, out var metadata))
        {
            return;
        }

        var sharedCount = Math.Min(contents.Count, media.Items.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            if (contents[index] is AgentMediaMessageContent existing &&
                existing.HasSource(media.Items[index]))
            {
                existing.RefreshMetadata();
                continue;
            }

            var replacement = CreateMediaContent(
                media,
                media.Items[index],
                index,
                metadata,
                message);
            var previous = contents[index];
            contents[index] = replacement;
            message.Contents[index] = replacement;
            previous.Dispose();
        }

        for (var index = contents.Count; index < media.Items.Count; index++)
        {
            var content = CreateMediaContent(
                media,
                media.Items[index],
                index,
                metadata,
                message);
            contents.Add(content);
            message.Contents.Add(content);
        }

        while (contents.Count > media.Items.Count)
        {
            var index = contents.Count - 1;
            var content = contents[index];
            contents.RemoveAt(index);
            message.Contents.RemoveAt(index);
            content.Dispose();
        }
    }

    private static AgentMediaMessageContent CreateMediaContent(
        MediaContentBlock block,
        DataContent item,
        int index,
        BlockProjectionMetadata metadata,
        ConversationMessage message)
    {
        var content = new AgentMediaMessageContent(
            block,
            item,
            index,
            metadata.Turn,
            metadata.IsRequest);
        content.AttachMessage(message);
        return content;
    }

    private readonly record struct BlockProjectionMetadata(
        ConversationTurn? Turn,
        bool IsRequest);
}

internal interface IAgentBlockContent : IDisposable
{
    ContentBlock Block { get; }

    ConversationTurn? Turn { get; }

    bool IsRequest { get; }

    void AttachMessage(ConversationMessage message);

    void NotifyChanged();
}

internal sealed class AgentBlockBinding : IDisposable
{
    private readonly Action _contentChanged;
    private ContentBlockChangedSubscription _subscription;
    private ConversationMessage? _message;
    private bool _disposed;

    public AgentBlockBinding(
        ContentBlock block,
        ConversationTurn? turn,
        bool isRequest,
        Action contentChanged)
    {
        Block = block ?? throw new ArgumentNullException(nameof(block));
        Turn = turn;
        IsRequest = isRequest;
        _contentChanged = contentChanged
            ?? throw new ArgumentNullException(nameof(contentChanged));
        _subscription = block.OnChanged(OnBlockChanged);
    }

    public ContentBlock Block { get; }

    public ConversationTurn? Turn { get; }

    public bool IsRequest { get; }

    public void AttachMessage(ConversationMessage message) =>
        _message = message;

    public void NotifyChanged() =>
        OnBlockChanged();

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
        _contentChanged();
    }
}

/// <summary>Neutral message content that retains an AI block needing a specialized body view.</summary>
internal sealed class AgentBlockContent : MessageContent, IAgentBlockContent
{
    private readonly AgentBlockBinding _binding;

    internal AgentBlockContent(
        ContentBlock block,
        ConversationTurn? turn,
        bool isRequest)
        : base(block?.Id)
    {
        ArgumentNullException.ThrowIfNull(block);
        Presentation = block is MediaContentBlock
            ? ChatContentPresentation.Bubble
            : ChatContentPresentation.Bare;
        _binding = new AgentBlockBinding(
            block,
            turn,
            isRequest,
            RaiseContentChanged);
    }

    public ContentBlock Block => _binding.Block;

    public ConversationTurn? Turn => _binding.Turn;

    public bool IsRequest => _binding.IsRequest;

    public void AttachMessage(ConversationMessage message) =>
        _binding.AttachMessage(message);

    public void NotifyChanged() =>
        _binding.NotifyChanged();

    public void Dispose() =>
        _binding.Dispose();
}

/// <summary>Maps an AI rich/text block into the provider-neutral text content primitive.</summary>
internal sealed class AgentTextMessageContent : TextMessageContent, IAgentBlockContent
{
    private readonly TextContentBlock _block;
    private readonly AgentBlockBinding _binding;

    internal AgentTextMessageContent(
        TextContentBlock block,
        ConversationTurn? turn,
        bool isRequest)
        : base(block?.RawText, block?.Id)
    {
        _block = block ?? throw new ArgumentNullException(nameof(block));
        _binding = new AgentBlockBinding(
            block,
            turn,
            isRequest,
            OnBlockChanged);
    }

    public ContentBlock Block => _binding.Block;

    public ConversationTurn? Turn => _binding.Turn;

    public bool IsRequest => _binding.IsRequest;

    public void AttachMessage(ConversationMessage message) =>
        _binding.AttachMessage(message);

    public void NotifyChanged() =>
        _binding.NotifyChanged();

    public void Dispose() =>
        _binding.Dispose();

    private void OnBlockChanged()
    {
        var text = _block.RawText;
        if (string.Equals(Text, text, StringComparison.Ordinal))
            RaiseContentChanged();
        else
            Text = text;
    }
}

/// <summary>Maps an AI rich block into structured text with a readable neutral fallback.</summary>
internal sealed class AgentStructuredTextMessageContent
    : StructuredTextMessageContent<IReadOnlyList<RichTextNode>>, IAgentBlockContent
{
    private readonly RichContentBlock _block;
    private readonly AgentBlockBinding _binding;

    internal AgentStructuredTextMessageContent(
        RichContentBlock block,
        ConversationTurn? turn,
        bool isRequest)
        : base(
            block?.RawText,
            block?.Content ?? throw new ArgumentNullException(nameof(block)),
            block.Id)
    {
        _block = block;
        _binding = new AgentBlockBinding(
            block,
            turn,
            isRequest,
            OnBlockChanged);
    }

    public ContentBlock Block => _binding.Block;

    public ConversationTurn? Turn => _binding.Turn;

    public bool IsRequest => _binding.IsRequest;

    public void AttachMessage(ConversationMessage message) =>
        _binding.AttachMessage(message);

    public void NotifyChanged() =>
        _binding.NotifyChanged();

    public void Dispose() =>
        _binding.Dispose();

    private void OnBlockChanged() =>
        Replace(_block.RawText, _block.Content);
}

/// <summary>Maps one AI media item into the provider-neutral media content primitive.</summary>
internal sealed class AgentMediaMessageContent : MediaMessageContent, IAgentBlockContent
{
    private readonly MediaContentBlock _block;
    private readonly DataContent _item;
    private readonly ConversationTurn? _turn;
    private readonly bool _isRequest;
    private ConversationMessage? _message;

    internal AgentMediaMessageContent(
        MediaContentBlock block,
        DataContent item,
        int index,
        ConversationTurn? turn,
        bool isRequest)
        : base(
            item?.Data ?? throw new ArgumentNullException(nameof(item)),
            string.IsNullOrWhiteSpace(item.MediaType)
                ? "application/octet-stream"
                : item.MediaType,
            CreateId(block, index))
    {
        _block = block ?? throw new ArgumentNullException(nameof(block));
        _item = item;
        _turn = turn;
        _isRequest = isRequest;
        RefreshMetadata();
    }

    public ContentBlock Block => _block;

    public ConversationTurn? Turn => _turn;

    public bool IsRequest => _isRequest;

    public void AttachMessage(ConversationMessage message) =>
        _message = message;

    public void NotifyChanged()
    {
        if (_message is not null)
        {
            _message.Status = Block.LifecycleState == BlockLifecycleState.Active
                ? ConversationMessageStatus.Sending
                : ConversationMessageStatus.Sent;
        }

        RefreshMetadata();
    }

    public void Dispose() =>
        _message = null;

    internal bool HasSource(DataContent item) =>
        ReferenceEquals(_item, item);

    internal void RefreshMetadata()
    {
        if (!string.Equals(FileName, _item.Name, StringComparison.Ordinal))
            FileName = _item.Name;
        if (!string.Equals(AltText, _item.Name, StringComparison.Ordinal))
            AltText = _item.Name;
    }

    private static string? CreateId(
        MediaContentBlock? block,
        int index) =>
        string.IsNullOrWhiteSpace(block?.Id)
            ? null
            : $"{block.Id}:{index}";
}
