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
    private const string StoppedPropertyName =
        "Microsoft.Maui.AI.Chat.Persistence.IsStopped.v1";
    private const string MarkerValue = "true";

    private readonly IChatClient _chatClient;
    private readonly UIAgentOptions _options;
    private readonly ILogger _logger;
    private readonly List<ChatMessage> _history = new();
    private int _historyVersion;
    private bool _disposed;

    internal UIAgentOptions Options => _options;

    internal void LogProcessingError(Exception exception) =>
        UIAgentLog.ProcessingError(_logger, exception);

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
        : this(chatClient, new UIAgentOptions(), loggerFactory)
    {
        configure?.Invoke(_options);
    }

    /// <summary>Initializes the agent with options that the caller configures afterwards.</summary>
    internal UIAgent(IChatClient chatClient, UIAgentOptions options, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _options = options;
        _logger = (ILogger?)loggerFactory?.CreateLogger<BlockMappingPipeline>() ?? NullLogger.Instance;
    }

    /// <summary>
    /// Clears the committed message history and the configured persistent conversation thread.
    /// </summary>
    /// <remarks>
    /// A send that is still running when the history is cleared is never committed. The agent is
    /// single-thread-affine; <see cref="AgentContext.ClearAsync"/> stops and awaits its active
    /// response before it clears.
    /// </remarks>
    public void ClearHistory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _historyVersion++;
        _history.Clear();
        _options.Thread?.Clear();
    }

    /// <summary>
    /// Sends one message and streams renderable blocks. A fully enumerated call commits one turn
    /// to the history and the configured thread; a failed, canceled, or abandoned enumeration
    /// commits nothing.
    /// </summary>
    public async IAsyncEnumerable<ContentBlock> SendMessageAsync(
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var turn = BeginTurn();
        await foreach (var block in SendRoundAsync(turn, [message], cancellationToken))
        {
            yield return block;
        }

        cancellationToken.ThrowIfCancellationRequested();
        CommitTurn(turn, stopped: false);
    }

    internal PendingTurn BeginTurn(ConversationTurn? displayTurn = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PendingTurn(_historyVersion, displayTurn);
    }

    /// <summary>
    /// Sends one request round of a pending turn and streams its blocks. Nothing is committed until
    /// <see cref="CommitTurn"/>.
    /// </summary>
    internal async IAsyncEnumerable<ContentBlock> SendRoundAsync(
        PendingTurn turn,
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));
        foreach (var message in messages)
            ArgumentNullException.ThrowIfNull(message);

        ThrowIfToolsCannotBeInvoked();

        // The request is recorded and shown even when the turn is already stopping, so a stopped turn keeps
        // it. The round stops before the request is sent.
        var requestMessages = messages
            .Select(EnsureMessageId)
            .ToArray();
        foreach (var requestMessage in requestMessages)
            turn.AddRequest(requestMessage);

        var conversationId = GetConversationId(_options.Thread, turn);
        var request = SendAnsweredUIActionsAsCalls(conversationId is not null
            ? requestMessages
            : [.. _history, .. turn.Messages]);

        var pipeline = new BlockMappingPipeline(_options, _logger, turn.Calls);
        try
        {
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
            pipeline.Finalize();

            cancellationToken.ThrowIfCancellationRequested();
            UIAgentLog.StreamingAssistantResponse(_logger);
            var chatOptions = BuildChatOptions(conversationId);
            var updateIndex = 0;
            await foreach (var update in _chatClient.GetStreamingResponseAsync(
                request,
                chatOptions,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var contentTypes = string.Join(", ", update.Contents.Select(c => c.GetType().Name));
                UIAgentLog.ReceivedUpdate(_logger, updateIndex++, update.Role?.Value, contentTypes);

                var processUpdate = ApplyStateMapper(update);
                turn.AddResponse(update, processUpdate);
                if (processUpdate.Contents.Count == 0 && update.Contents.Count > 0)
                    continue;

                await foreach (var block in pipeline.Process(processUpdate, cancellationToken))
                {
                    yield return block;
                }
            }

            UIAgentLog.StreamComplete(_logger, updateIndex);
        }
        finally
        {
            // A stopped round keeps its blocks, so they must not stay active.
            pipeline.Finalize();
        }

        cancellationToken.ThrowIfCancellationRequested();
        turn.CompleteRound();
    }

    /// <summary>
    /// Writes a pending turn to the configured thread and then commits it to the history.
    /// </summary>
    /// <param name="turn">The turn to commit.</param>
    /// <param name="stopped">
    /// Whether the turn was stopped before it completed. A stopped turn keeps its partial output,
    /// is marked as stopped in the thread, and does not advance a stateful provider conversation.
    /// </param>
    /// <remarks>
    /// Function calls without a result are never committed. For a stopped turn the same applies to
    /// approval requests and responses whose call has no result, and the history also leaves out
    /// reasoning that the stop cut off (<see cref="CutOffReasoning"/>). A turn that became stale because
    /// the history was cleared or restored after it started is discarded.
    /// </remarks>
    internal void CommitTurn(PendingTurn turn, bool stopped)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (turn.HistoryVersion != _historyVersion || turn.InitialMessage is not { } initialMessage)
            return;

        turn.CompleteRound();
        var answeredCallIds = UnansweredCalls.CollectAnsweredCallIds(turn);

        var thread = _options.Thread;
        if (thread is not null)
        {
            thread.AppendUserMessage(stopped
                ? MarkStopped(initialMessage)
                : initialMessage);
            foreach (var update in turn.ThreadUpdates)
            {
                var committed = UnansweredCalls.Remove(
                    update,
                    answeredCallIds,
                    includeApprovals: stopped);
                if (committed is null)
                    continue;

                if (stopped && committed.ConversationId is not null)
                {
                    committed = committed.Clone();
                    committed.ConversationId = null;
                }

                thread.AppendUpdate(committed);
            }

            thread.CompleteTurn();
        }

        var messages = UnansweredCalls.Remove(
            turn.Messages,
            answeredCallIds,
            includeApprovals: stopped);
        if (stopped)
            CutOffReasoning.Remove(messages);
        _history.AddRange(messages);
        UIAgentLog.AddedToHistory(_logger, messages.Count);
    }

    /// <summary>
    /// Replays the configured thread's committed raw updates through newly-created block pipelines.
    /// </summary>
    /// <remarks>
    /// Restore only reconstructs renderable history; it does not invoke tools or resume
    /// interactive blocks. It also rebuilds the message history sent with the next request.
    /// Custom projections require the same handlers to be registered, and their
    /// discriminator data must survive the thread implementation's serialization. In particular,
    /// <see cref="AIContent.RawRepresentation"/> and <see cref="ChatResponseUpdate.RawRepresentation"/>
    /// are not durable discriminators unless the implementation explicitly persists them. Thread
    /// implementations must also round-trip <see cref="ChatResponseUpdate.AdditionalProperties"/>,
    /// which carries engine metadata for continuation rounds and stopped turns.
    /// </remarks>
    public async Task<IReadOnlyList<ContentBlock>> RestoreAsync(
        CancellationToken cancellationToken = default)
        => Flatten(await RestoreTurnsAsync(cancellationToken));

    /// <summary>
    /// Loads an existing conversation, such as a recorded or imported one: replays its messages
    /// through newly-created block pipelines and makes them the history that the next
    /// <see cref="SendMessageAsync"/> continues.
    /// </summary>
    /// <param name="messages">The conversation's messages in order.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>The blocks that show the conversation.</returns>
    /// <remarks>
    /// <para>
    /// Each user message starts a turn, except one that only answers approvals or tool calls. Nothing
    /// is invoked: tool calls and UI actions are shown with their recorded results, and approvals and
    /// UI actions are not resumed. System messages are part of the history but are not shown.
    /// </para>
    /// <para>
    /// The history keeps the messages as given, except tool calls without a recorded result and their
    /// approvals, which a provider would reject or execute. A send that is still running is never
    /// committed. This overload cannot be used with a configured <see cref="IConversationThread"/>,
    /// which is the source of truth for its conversation.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ContentBlock>> RestoreAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
        => Flatten(await RestoreTurnsAsync(messages, cancellationToken));

    internal Task<IReadOnlyList<RestoredTurn>> RestoreTurnsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var thread = _options.Thread;
        return thread is null
            ? Task.FromResult<IReadOnlyList<RestoredTurn>>(Array.Empty<RestoredTurn>())
            : ReplayAsync(thread.GetUpdates(), history: null, cancellationToken);
    }

    internal Task<IReadOnlyList<RestoredTurn>> RestoreTurnsAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.Thread is not null)
        {
            throw new InvalidOperationException(
                "A conversation cannot be restored from messages when UIAgentOptions.Thread is set. " +
                "Restore from the thread instead.");
        }

        var history = messages.ToArray();
        foreach (var message in history)
        {
            if (message is null)
                throw new ArgumentException("The messages cannot contain null.", nameof(messages));
        }

        var updates = history
            .Select(message => CreateMessageUpdate(EnsureMessageId(message), isContinuation: false))
            .ToArray();
        var answered = new HashSet<string>(
            history
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(result => result.CallId),
            StringComparer.Ordinal);
        return ReplayAsync(
            updates,
            UnansweredCalls.Remove(history, answered, includeApprovals: true),
            cancellationToken);
    }

    private static List<ContentBlock> Flatten(IReadOnlyList<RestoredTurn> turns)
    {
        var blocks = new List<ContentBlock>();
        foreach (var turn in turns)
        {
            blocks.AddRange(turn.RequestBlocks);
            blocks.AddRange(turn.ResponseBlocks);
        }

        return blocks;
    }

    /// <summary>Rebuilds turns, typed state and the history from committed updates.</summary>
    /// <param name="updates">The updates to replay.</param>
    /// <param name="history">The history to use, or <see langword="null"/> to rebuild it from the updates.</param>
    /// <param name="cancellationToken">Cancels the replay.</param>
    private async Task<IReadOnlyList<RestoredTurn>> ReplayAsync(
        IReadOnlyList<ChatResponseUpdate> updates,
        IReadOnlyList<ChatMessage>? history,
        CancellationToken cancellationToken)
    {
        var previousHistory = _history.ToArray();
        var stateCheckpoint = CaptureStateCheckpoint();
        var turns = new List<RestoredTurn>();
        var replayedHistory = new List<ChatMessage>();
        var responseUpdates = new List<ChatResponseUpdate>();
        RestoredTurn? currentTurn = null;
        BlockMappingPipeline? responsePipeline = null;
        FunctionCallRegistry? calls = null;
        try
        {
            BeginStateRestore();
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Instructions are history, not part of a turn.
                if (update.Role == ChatRole.System)
                {
                    FlushResponseHistory();
                    replayedHistory.Add(CreateHistoryMessage(update));
                    continue;
                }

                var isContinuation = IsContinuation(update);
                if (update.Role == ChatRole.User && !isContinuation)
                {
                    CompleteRestoredTurn();
                    currentTurn = new RestoredTurn(
                        update.MessageId
                            ?? update.ResponseId
                            ?? Guid.NewGuid().ToString("N"),
                        IsStopped(update));
                    turns.Add(currentTurn);
                    replayedHistory.Add(CreateHistoryMessage(update));
                    calls = new FunctionCallRegistry();

                    var requestPipeline = new BlockMappingPipeline(
                        _options,
                        _logger,
                        calls);
                    await foreach (var block in requestPipeline.Process(
                        update,
                        cancellationToken))
                    {
                        currentTurn.RequestBlocks.Add(block);
                    }
                    requestPipeline.Finalize();
                    responsePipeline = new BlockMappingPipeline(
                        _options,
                        _logger,
                        calls);
                    continue;
                }

                if (currentTurn is null || responsePipeline is null)
                {
                    currentTurn = new RestoredTurn(
                        update.MessageId
                            ?? update.ResponseId
                            ?? Guid.NewGuid().ToString("N"),
                        isStopped: false);
                    turns.Add(currentTurn);
                    calls = new FunctionCallRegistry();
                    responsePipeline = new BlockMappingPipeline(
                        _options,
                        _logger,
                        calls);
                }

                // Continuation inputs were requests, so like live requests they bypass the state mapper.
                var processUpdate = isContinuation
                    ? update
                    : ApplyStateMapper(update);
                if (!isContinuation)
                    currentTurn.ObserveResponse(update);
                responseUpdates.Add(processUpdate);
                if (processUpdate.Contents.Count > 0
                    || update.Contents.Count == 0)
                {
                    await foreach (var block in responsePipeline.Process(
                        processUpdate,
                        cancellationToken))
                    {
                        currentTurn.ResponseBlocks.Add(block);
                    }
                }

                RestoreApprovalResponses(update, currentTurn);
            }

            CompleteRestoredTurn();
            _history.Clear();
            _history.AddRange(history ?? replayedHistory);
            _historyVersion++;
            CompleteStateRestore();

            // Restored approvals and UI actions are history: a decision or result could not be sent, and a call
            // without a recorded result never gets one. Nothing shows these blocks yet, so the change needs no
            // notification.
            foreach (var turn in turns)
            {
                foreach (var block in turn.RequestBlocks.Concat(turn.ResponseBlocks))
                {
                    if (block is InteractiveFunctionBlock interactive)
                    {
                        interactive.Abandon();
                        interactive.CaptureAwaitingInput();
                        interactive.InnerBlock.Abandon();
                    }
                    else if (block is FunctionInvocationContentBlock call)
                    {
                        call.Abandon();
                    }
                }
            }

            return turns;
        }
        catch
        {
            _history.Clear();
            _history.AddRange(previousHistory);
            RestoreStateCheckpoint(stateCheckpoint);
            throw;
        }

        void CompleteRestoredTurn()
        {
            responsePipeline?.Finalize();
            responsePipeline = null;
            FlushResponseHistory(stopped: currentTurn?.IsStopped == true);
        }

        void FlushResponseHistory(bool stopped = false)
        {
            if (responseUpdates.Count == 0)
                return;

            // The same history as the live commit of the turn.
            var messages = responseUpdates.ToChatResponse().Messages.ToList();
            if (stopped)
                CutOffReasoning.Remove(messages);
            replayedHistory.AddRange(messages);
            responseUpdates.Clear();
        }
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
        stateContext = context;
        _options.StateMapper(context);
        return context.HasHandledContent ? context.GetFilteredUpdate() : update;
    }

    /// <summary>
    /// Throws when the configured tools include an invocable <see cref="AIFunction"/>, or a UI action
    /// that requires approval, but no <see cref="FunctionInvokingChatClient"/> is present to invoke the
    /// tool or to request the approval.
    /// </summary>
    internal void ThrowIfToolsCannotBeInvoked()
    {
        bool? hasFunctionInvocation = null;
        foreach (var tool in _options.ChatOptions?.Tools ?? [])
        {
            if (tool is AIFunction function
                && !_options.UIActions.ContainsKey(function.Name)
                && !HasFunctionInvocation())
            {
                throw new InvalidOperationException(
                    $"ChatOptions.Tools contains the invocable function '{function.Name}', but the chat " +
                    "client has no function invocation. Build the client with " +
                    "'.AsBuilder().UseFunctionInvocation().Build()' so tools are invoked, or register " +
                    "client-side functions with UIAgentOptions.RegisterUIAction.");
            }
        }

        foreach (var registration in _options.UIActions.Values)
        {
            // Without the function invoking client nothing asks for the approval, so the action would
            // run without it.
            if (registration.RequiresApproval && !HasFunctionInvocation())
            {
                throw new InvalidOperationException(
                    $"The UI action '{registration.Function.Name}' requires approval, but the chat client has " +
                    "no function invocation to request it. Build the client with " +
                    "'.AsBuilder().UseFunctionInvocation().Build()'.");
            }
        }

        bool HasFunctionInvocation()
            => hasFunctionInvocation ??= _chatClient.GetService<FunctionInvokingChatClient>() is not null;
    }

    /// <summary>Creates the error result returned to the model for a call nobody executed.</summary>
    internal FunctionResultContent CreateUnansweredCallResult(FunctionCallContent call)
    {
        UIAgentLog.UnansweredToolCall(_logger, call.Name, call.CallId);
        return new FunctionResultContent(
            call.CallId,
            "Error: The tool call was not executed. Call the tool again if its result is still needed.");
    }

    /// <summary>
    /// Gets the provider conversation to continue. Only a configured thread makes a conversation
    /// stateful; within a turn the latest identifier returned by the provider wins.
    /// </summary>
    private static string? GetConversationId(IConversationThread? thread, PendingTurn turn)
    {
        if (thread is null)
            return null;

        return turn.ConversationId
            ?? (thread.IsStateful ? thread.ConversationId : null);
    }

    private ChatOptions? BuildChatOptions(string? conversationId)
    {
        if (conversationId is null && _options.UIActions.Count == 0)
            return _options.ChatOptions;

        var chatOptions = _options.ChatOptions?.Clone() ?? new ChatOptions();
        // ChatOptions.Clone does not retain this transport customization on all M.E.AI versions.
        chatOptions.RawRepresentationFactory = _options.ChatOptions?.RawRepresentationFactory;
        if (conversationId is not null)
            chatOptions.ConversationId = conversationId;

        if (_options.UIActions.Count > 0)
        {
            var tools = chatOptions.Tools is null
                ? new List<AITool>()
                : [.. chatOptions.Tools];

            foreach (var registration in _options.UIActions.Values)
            {
                var action = registration.Function;
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

    private static ChatMessage EnsureMessageId(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.MessageId))
            return message;

        var clone = message.Clone();
        clone.MessageId = Guid.NewGuid().ToString("N");
        return clone;
    }

    /// <summary>
    /// Sends the approval requests that <see cref="FunctionInvokingChatClient"/> created for UI actions as
    /// the calls they carry once the client has answered them, and leaves out their approval responses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When one call of a response needs approval, the function invoking client turns every call of the
    /// response into an approval request, UI actions included. It cannot invoke a UI action, so the client
    /// runs the action and sends its result. The function invoking client would remove such a request and
    /// skip its response because the call already has a result, so without this the result would reach
    /// the model without its call. A call that the function invoking client answered itself, such as a
    /// rejected one, is already in the messages as a call and keeps its request.
    /// </para>
    /// <para>
    /// Providers that number their calls per response reuse call ids, and the function invoking client
    /// derives the request id from the call id, so neither identifies a request across the history. Each
    /// request is matched in message order with what follows it: the client answered it when a result for
    /// its call id arrives before the call is sent again, and its response is the next response with its
    /// request id.
    /// </para>
    /// </remarks>
    private ChatMessage[] SendAnsweredUIActionsAsCalls(ChatMessage[] messages)
    {
        if (_options.UIActions.Count == 0)
            return messages;

        HashSet<ToolApprovalRequestContent>? answered = null;
        Dictionary<string, ToolApprovalRequestContent>? awaiting = null;
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent { ToolCall: FunctionCallContent call } request
                        when IsUIAction(call):
                        (awaiting ??= new(StringComparer.Ordinal))[call.CallId] = request;
                        break;

                    // The function invoking client answered the request itself and sent its call.
                    case FunctionCallContent call:
                        awaiting?.Remove(call.CallId);
                        break;

                    case FunctionResultContent result:
                        if (awaiting is not null && awaiting.Remove(result.CallId, out var answeredRequest))
                            (answered ??= new(ReferenceEqualityComparer.Instance)).Add(answeredRequest);
                        break;
                }
            }
        }

        if (answered is null)
            return messages;

        HashSet<string>? skippedResponses = null;
        List<ChatMessage>? rewritten = null;
        for (var i = 0; i < messages.Length; i++)
        {
            var message = messages[i];
            List<AIContent>? contents = null;
            for (var j = 0; j < message.Contents.Count; j++)
            {
                var content = message.Contents[j];
                var replacement = content;
                if (content is ToolApprovalRequestContent { ToolCall: FunctionCallContent requestCall } request
                    && IsUIAction(requestCall))
                {
                    skippedResponses ??= new(StringComparer.Ordinal);
                    if (answered.Contains(request))
                    {
                        replacement = requestCall;
                        skippedResponses.Add(request.RequestId);
                    }
                    else
                    {
                        skippedResponses.Remove(request.RequestId);
                    }
                }
                else if (content is ToolApprovalResponseContent { ToolCall: FunctionCallContent responseCall } response
                    && IsUIAction(responseCall)
                    && skippedResponses?.Remove(response.RequestId) == true)
                {
                    replacement = null;
                }

                if (contents is null && ReferenceEquals(replacement, content))
                    continue;

                contents ??= [.. message.Contents.Take(j)];
                if (replacement is not null)
                    contents.Add(replacement);
            }

            if (contents is null)
            {
                rewritten?.Add(message);
                continue;
            }

            rewritten ??= [.. messages.Take(i)];
            if (contents.Count > 0)
            {
                var clone = message.Clone();
                clone.Contents = contents;
                rewritten.Add(clone);
            }
        }

        return rewritten is null ? messages : [.. rewritten];

        bool IsUIAction(FunctionCallContent call) => _options.UIActions.ContainsKey(call.Name);
    }

    internal static ChatResponseUpdate CreateMessageUpdate(
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
                additionalProperties[ContinuationPropertyName] = MarkerValue;
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

    /// <summary>
    /// Gets whether an update is a request that continues its turn, rather than a user message that
    /// starts one: either marked as such, or a user update that only answers approvals or tool calls.
    /// </summary>
    private static bool IsContinuation(ChatResponseUpdate update)
        => HasMarker(update.AdditionalProperties, ContinuationPropertyName)
            || (update.Role == ChatRole.User
                && update.Contents.Count > 0
                && update.Contents.All(
                    content => content is ToolApprovalResponseContent or FunctionResultContent));

    private static bool IsStopped(ChatResponseUpdate update)
        => HasMarker(update.AdditionalProperties, StoppedPropertyName);

    private static bool HasMarker(
        AdditionalPropertiesDictionary? properties,
        string name)
        => properties is not null
            && properties.TryGetValue(name, out var value)
            && value is string text
            && string.Equals(text, MarkerValue, StringComparison.Ordinal);

    private static ChatMessage MarkStopped(ChatMessage message)
    {
        var marked = message.Clone();
        marked.AdditionalProperties = message.AdditionalProperties is null
            ? new AdditionalPropertiesDictionary()
            : new AdditionalPropertiesDictionary(message.AdditionalProperties);
        marked.AdditionalProperties[StoppedPropertyName] = MarkerValue;
        return marked;
    }

    private static ChatMessage CreateHistoryMessage(ChatResponseUpdate update)
    {
        AdditionalPropertiesDictionary? additionalProperties = null;
        if (update.AdditionalProperties is { Count: > 0 } properties)
        {
            additionalProperties = new AdditionalPropertiesDictionary(properties);
            additionalProperties.Remove(StoppedPropertyName);
            additionalProperties.Remove(ContinuationPropertyName);
            if (additionalProperties.Count == 0)
                additionalProperties = null;
        }

        return new ChatMessage(update.Role ?? ChatRole.User, [.. update.Contents])
        {
            AuthorName = update.AuthorName,
            CreatedAt = update.CreatedAt,
            MessageId = update.MessageId,
            RawRepresentation = update.RawRepresentation,
            AdditionalProperties = additionalProperties,
        };
    }

    private static void RestoreApprovalResponses(
        ChatResponseUpdate update,
        RestoredTurn turn)
    {
        foreach (var content in update.Contents)
        {
            if (content is not ToolApprovalResponseContent response)
                continue;

            var blocks = turn.ResponseBlocks;
            for (var i = blocks.Count - 1; i >= 0; i--)
            {
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
