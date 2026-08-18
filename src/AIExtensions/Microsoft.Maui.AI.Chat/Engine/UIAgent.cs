// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Wraps a Microsoft.Extensions.AI <see cref="IChatClient"/>: sends messages, streams the response, owns the
/// chat history, and runs each update through the <see cref="BlockMappingPipeline"/> to yield
/// <see cref="ContentBlock"/>s.
/// </summary>
/// <remarks>
/// Configured with <see cref="UIAgentOptions"/> (instructions, tools, persistence, custom handlers).
/// The stateful, UI-facing wrapper on top of it is <see cref="AgentContext"/>.
/// <para>
/// This type is single-thread-affine and is not thread-safe. Callers must serialize access and
/// enter the owning application thread before calling it.
/// </para>
/// </remarks>
public class UIAgent : IDisposable
{
    private const string ContinuationPropertyName =
        "Microsoft.Maui.AI.Chat.Persistence.IsContinuation.v1";
    private const string ContinuationPropertyValue = "true";

    private readonly IChatClient _chatClient;
    private readonly UIAgentOptions _options;
    private readonly ILogger _logger;
    private readonly List<ChatMessage> _history = new();
    private bool _disposed;

    internal UIAgentOptions Options => _options;

    internal virtual object? AgentStateObject => null;

    public UIAgent(IChatClient chatClient)
        : this(chatClient, configure: null)
    {
    }

    public UIAgent(IChatClient chatClient, ChatOptions chatOptions)
        : this(chatClient, options => options.ChatOptions = chatOptions)
    {
    }

    public UIAgent(IChatClient chatClient, ChatOptions chatOptions, ILoggerFactory? loggerFactory)
        : this(chatClient, options => options.ChatOptions = chatOptions, loggerFactory)
    {
    }

    public UIAgent(IChatClient chatClient, Action<UIAgentOptions>? configure)
        : this(chatClient, configure, loggerFactory: null)
    {
    }

    public UIAgent(IChatClient chatClient, Action<UIAgentOptions>? configure, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _options = new UIAgentOptions();
        configure?.Invoke(_options);
        _logger = (ILogger?)loggerFactory?.CreateLogger<BlockMappingPipeline>() ?? NullLogger.Instance;
    }

    /// <summary>
    /// Clears local message history and the configured persistent conversation thread.
    /// </summary>
    /// <remarks>
    /// The agent is single-thread-affine. Cancel and await an active send before starting another.
    /// </remarks>
    public void ClearHistory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _history.Clear();
        _options.Thread?.Clear();
    }

    internal readonly record struct HistoryCheckpoint(int Count);

    internal HistoryCheckpoint CaptureHistoryCheckpoint() => new(_history.Count);

    internal void RestoreHistory(HistoryCheckpoint checkpoint)
    {
        if (checkpoint.Count < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint));

        if (checkpoint.Count > _history.Count)
            return;

        _history.RemoveRange(checkpoint.Count, _history.Count - checkpoint.Count);
    }

    internal void RollbackHistory(HistoryCheckpoint checkpoint, ChatMessage requestMessage)
    {
        if (checkpoint.Count < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint));

        if (checkpoint.Count > _history.Count)
            return;

        var requestWasAdded = _history.Count > checkpoint.Count;
        _history.RemoveRange(checkpoint.Count, _history.Count - checkpoint.Count);
        if (requestWasAdded)
            _history.Add(requestMessage);
    }

    /// <summary>
    /// Sends one message and streams renderable blocks. When used directly, one successful call
    /// commits one persistent conversation turn.
    /// </summary>
    public IAsyncEnumerable<ContentBlock> SendMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken = default)
        => SendMessagesAsync(
            [message],
            startsThreadTurn: true,
            completesThreadTurn: true,
            cancellationToken);

    internal IAsyncEnumerable<ContentBlock> SendMessageAsync(
        ChatMessage message,
        bool startsThreadTurn,
        bool completesThreadTurn,
        CancellationToken cancellationToken = default) =>
        SendMessagesAsync(
            [message],
            startsThreadTurn,
            completesThreadTurn,
            cancellationToken);

    internal async IAsyncEnumerable<ContentBlock> SendMessagesAsync(
        IReadOnlyList<ChatMessage> messages,
        bool startsThreadTurn,
        bool completesThreadTurn,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));
        foreach (var message in messages)
            ArgumentNullException.ThrowIfNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        var requestMessages = messages
            .Select(EnsureMessageId)
            .ToArray();
        var thread = _options.Thread;

        for (var index = 0; index < requestMessages.Length; index++)
        {
            var requestMessage = requestMessages[index];
            if (startsThreadTurn && index == 0)
            {
                thread?.AppendUserMessage(requestMessage);
                RefreshHistoryFromThread(thread);
            }
            else
            {
                thread?.AppendUpdate(CreateMessageUpdate(
                    requestMessage,
                    isContinuation: true));
            }

            _history.Add(requestMessage);
        }

        ChatMessage[] historySnapshot = thread is { IsStateful: true }
            ? requestMessages
            : [.. _history];

        var pipeline = new BlockMappingPipeline(_options, _logger);
        foreach (var requestMessage in requestMessages)
        {
            var userUpdate = CreateMessageUpdate(
                requestMessage,
                isContinuation: false);
            await foreach (var block in pipeline.Process(
                userUpdate,
                cancellationToken))
            {
                yield return block;
            }
        }
        foreach (var block in pipeline.Finalize())
        {
            yield return block;
        }

        UIAgentLog.StreamingAssistantResponse(_logger);
        var assistantUpdates = new List<ChatResponseUpdate>();
        var chatOptions = BuildChatOptions(thread);

        var updateIndex = 0;
        await foreach (var update in _chatClient.GetStreamingResponseAsync(
            historySnapshot,
            chatOptions,
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contentTypes = string.Join(", ", update.Contents.Select(c => c.GetType().Name));
            UIAgentLog.ReceivedUpdate(_logger, updateIndex++, update.Role?.Value, contentTypes);

            var processUpdate = ApplyStateMapper(update);
            assistantUpdates.Add(processUpdate);
            thread?.AppendUpdate(update);
            if (processUpdate.Contents.Count == 0 && update.Contents.Count > 0)
                continue;

            await foreach (var block in pipeline.Process(processUpdate, cancellationToken))
            {
                yield return block;
            }
        }

        UIAgentLog.StreamComplete(_logger, assistantUpdates.Count);

        foreach (var block in pipeline.Finalize())
        {
            yield return block;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var response = assistantUpdates.ToChatResponse();
        if (completesThreadTurn)
            thread?.CompleteTurn();

        foreach (var responseMessage in response.Messages)
            _history.Add(responseMessage);

        UIAgentLog.AddedToHistory(_logger, response.Messages.Count);
    }

    /// <summary>
    /// Replays the configured thread's committed raw updates through newly-created block pipelines.
    /// </summary>
    /// <remarks>
    /// Restore only reconstructs renderable history; it does not invoke backend tools or resume
    /// interactive blocks. Custom projections require the same handlers to be registered, and their
    /// discriminator data must survive the thread implementation's serialization. In particular,
    /// <see cref="AIContent.RawRepresentation"/> and <see cref="ChatResponseUpdate.RawRepresentation"/>
    /// are not durable discriminators unless the implementation explicitly persists them. Thread
    /// implementations must also round-trip <see cref="ChatResponseUpdate.AdditionalProperties"/>,
    /// which carries engine metadata for continuation rounds.
    /// </remarks>
    public async Task<IReadOnlyList<ContentBlock>> RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var thread = _options.Thread;
        if (thread is null)
            return Array.Empty<ContentBlock>();

        var previousHistory = _history.ToArray();
        var stateCheckpoint = CaptureStateCheckpoint();
        try
        {
            BeginStateRestore();
            var updates = thread.GetUpdates();
            if (updates.Count == 0)
            {
                _history.Clear();
                CompleteStateRestore();
                return Array.Empty<ContentBlock>();
            }

            var restoredHistory = thread.IsStateful
                ? Array.Empty<ChatMessage>()
                : thread.GetMessageHistory();
            var blocks = new List<ContentBlock>();
            BlockMappingPipeline? responsePipeline = null;
            string? currentTurnId = null;
            var nextBlockStartsTurn = false;

            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startsTurn = update.Role == ChatRole.User
                    && !IsContinuation(update);
                if (startsTurn)
                {
                    responsePipeline?.Finalize();
                    currentTurnId = update.MessageId
                        ?? update.ResponseId
                        ?? Guid.NewGuid().ToString("N");
                    nextBlockStartsTurn = true;

                    var requestPipeline = new BlockMappingPipeline(
                        _options,
                        _logger);
                    await foreach (var block in requestPipeline.Process(
                        update,
                        cancellationToken))
                    {
                        AddRestoredBlock(
                            blocks,
                            block,
                            currentTurnId,
                            isRequest: true,
                            ref nextBlockStartsTurn);
                    }
                    requestPipeline.Finalize();
                    responsePipeline = new BlockMappingPipeline(
                        _options,
                        _logger);
                    continue;
                }

                if (responsePipeline is null)
                {
                    currentTurnId = update.MessageId
                        ?? update.ResponseId
                        ?? Guid.NewGuid().ToString("N");
                    nextBlockStartsTurn = true;
                    responsePipeline = new BlockMappingPipeline(
                        _options,
                        _logger);
                }

                var processUpdate = ApplyStateMapper(update);
                if (processUpdate.Contents.Count > 0
                    || update.Contents.Count == 0)
                {
                    await foreach (var block in responsePipeline.Process(
                        processUpdate,
                        cancellationToken))
                    {
                        AddRestoredBlock(
                            blocks,
                            block,
                            currentTurnId!,
                            isRequest: false,
                            ref nextBlockStartsTurn);
                    }
                }

                RestoreApprovalResponses(update, blocks, currentTurnId!);
            }

            responsePipeline?.Finalize();
            _history.Clear();
            _history.AddRange(restoredHistory);
            CompleteStateRestore();
            return blocks;
        }
        catch
        {
            _history.Clear();
            _history.AddRange(previousHistory);
            RestoreStateCheckpoint(stateCheckpoint);
            throw;
        }
    }

    internal void CompleteThreadTurn()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _options.Thread?.CompleteTurn();
    }

    internal virtual ChatResponseUpdate ApplyStateMapper(ChatResponseUpdate update)
        => ApplyStateMapper(update, out _);

    internal virtual void RejectPendingPredictiveState()
    {
    }

    internal virtual object? CaptureStateCheckpoint() => null;

    internal virtual void BeginStateRestore()
    {
    }

    internal virtual void CompleteStateRestore()
    {
    }

    internal virtual void RestoreStateCheckpoint(object? checkpoint)
    {
    }

    internal virtual void ResetState()
    {
    }

    internal ChatResponseUpdate ApplyStateMapper(
        ChatResponseUpdate update,
        out StateMapperContext? stateContext)
    {
        stateContext = null;
        if (_options.StateMapper is null)
            return update;

        var context = new StateMapperContext(update);
        if (!_options.StateMapper(context))
            return update;

        stateContext = context;
        return context.HasHandledContent ? context.GetFilteredUpdate() : update;
    }

    internal async Task<FunctionResultContent> InvokeToolAsync(
        FunctionCallContent call, CancellationToken cancellationToken)
    {
        var function = FindBackendFunction(call.Name);
        if (function is null)
        {
            UIAgentLog.BackendFunctionNotFound(_logger, call.Name);
            return new FunctionResultContent(call.CallId, $"Error: Function '{call.Name}' not found.");
        }

        UIAgentLog.InvokingBackendFunction(_logger, call.Name, call.CallId);
        var args = call.Arguments is not null ? new AIFunctionArguments(call.Arguments) : null;
        var result = await function.InvokeAsync(args, cancellationToken);
        return new FunctionResultContent(call.CallId, result);
    }

    private void RefreshHistoryFromThread(IConversationThread? thread)
    {
        if (thread is null)
            return;

        _history.Clear();
        if (!thread.IsStateful)
            _history.AddRange(thread.GetMessageHistory());
    }

    private ChatOptions? BuildChatOptions(IConversationThread? thread)
    {
        var conversationId = thread is { IsStateful: true }
            ? thread.ConversationId
            : null;
        if (conversationId is null && _options.UIActions.Count == 0)
            return _options.ChatOptions;

        var chatOptions = _options.ChatOptions?.Clone() ?? new ChatOptions();
        if (conversationId is not null)
            chatOptions.ConversationId = conversationId;

        if (_options.UIActions.Count > 0)
        {
            var tools = chatOptions.Tools is null
                ? new List<AITool>()
                : [.. chatOptions.Tools];

            foreach (var action in _options.UIActions.Values)
            {
                if (tools.Any(tool => string.Equals(
                    tool.Name,
                    action.Name,
                    StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"UI action '{action.Name}' conflicts with an existing chat tool.");
                }
                tools.Add(action.AsDeclarationOnly());
            }
            chatOptions.Tools = tools;
        }

        return chatOptions;
    }

    internal void ApplyFunctionResult(
        FunctionInvocationContentBlock block,
        FunctionResultContent result)
    {
        foreach (var registration in _options.HandlerRegistrations)
        {
            if (registration.TryApplyFunctionResult(block, result))
                return;
        }
    }

    private AIFunction? FindBackendFunction(string name)
    {
        if (_options.ChatOptions?.Tools is null)
            return null;

        foreach (var tool in _options.ChatOptions.Tools)
        {
            if (tool is AIFunction function && function.Name == name)
                return function;
        }

        return null;
    }

    private static ChatMessage EnsureMessageId(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.MessageId))
            return message;

        var clone = message.Clone();
        clone.MessageId = Guid.NewGuid().ToString("N");
        return clone;
    }

    private static ChatResponseUpdate CreateMessageUpdate(
        ChatMessage message,
        bool isContinuation)
    {
        AdditionalPropertiesDictionary? additionalProperties = null;
        if (message.AdditionalProperties is not null || isContinuation)
        {
            additionalProperties = message.AdditionalProperties is null
                ? new AdditionalPropertiesDictionary()
                : new AdditionalPropertiesDictionary(message.AdditionalProperties);

            if (isContinuation)
                additionalProperties[ContinuationPropertyName] = ContinuationPropertyValue;
        }

        return new ChatResponseUpdate
        {
            Role = message.Role,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            Contents = [.. message.Contents],
            RawRepresentation = message.RawRepresentation,
            AdditionalProperties = additionalProperties,
        };
    }

    private static bool IsContinuation(ChatResponseUpdate update)
        => (update.AdditionalProperties is not null
                && update.AdditionalProperties.TryGetValue(
                    ContinuationPropertyName,
                    out var value)
                && value is string text
                && string.Equals(
                    text,
                    ContinuationPropertyValue,
                    StringComparison.Ordinal))
            || (update.Contents.Count > 0
                && update.Contents.All(
                    content => content is ToolApprovalResponseContent));

    private static void AddRestoredBlock(
        List<ContentBlock> blocks,
        ContentBlock block,
        string turnId,
        bool isRequest,
        ref bool nextBlockStartsTurn)
    {
        block.RestoredTurnId = turnId;
        block.StartsRestoredTurn = nextBlockStartsTurn;
        block.IsRestoredRequest = isRequest;
        nextBlockStartsTurn = false;
        blocks.Add(block);
    }

    private static void RestoreApprovalResponses(
        ChatResponseUpdate update,
        List<ContentBlock> blocks,
        string turnId)
    {
        foreach (var content in update.Contents)
        {
            if (content is not ToolApprovalResponseContent response)
                continue;

            for (var i = blocks.Count - 1; i >= 0; i--)
            {
                if (blocks[i].RestoredTurnId != turnId)
                    break;

                if (blocks[i] is ToolApprovalBlock approval
                    && approval.ApprovalRequest.RequestId == response.RequestId)
                {
                    approval.RestoreResponse(response);
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
