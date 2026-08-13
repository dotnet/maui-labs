using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// A <see cref="IChatClient"/> that adapts the GitHub Copilot SDK. It owns a single shared
/// <see cref="CopilotClient"/> and creates request-scoped Copilot sessions, streams runtime events
/// as <see cref="ChatResponseUpdate"/> values, and disposes each session when its turn ends. A session
/// remains active only while an external tool waits for its M.E.AI result. Durable completed-turn
/// state lives in the runtime and is addressed through
/// <see cref="ChatOptions.ConversationId"/> (the Copilot session id, echoed on every response).
/// </summary>
/// <remarks>
/// <para><b>Stateful conversations.</b> When <see cref="ChatOptions.ConversationId"/> is <see langword="null"/>
/// a new session is created; when it is set the session is resumed. The session id is emitted as the
/// <see cref="ChatResponse.ConversationId"/>/<see cref="ChatResponseUpdate.ConversationId"/> so callers can
/// continue the conversation on a later call. The adapter keeps no hidden single conversation.</para>
/// <para><b>Tool calling.</b> Tools supplied through <see cref="ChatOptions.Tools"/> are represented by
/// SDK-invocable proxy functions. A proxy surfaces the SDK request as <see cref="FunctionCallContent"/>
/// and waits while the outer Microsoft.Extensions.AI loop invokes the caller's real function. The next
/// call supplies <see cref="FunctionResultContent"/> to the waiting proxy, after which the same SDK session
/// continues. This prevents native double invocation and composes with <c>FunctionInvokingChatClient</c>.
/// A pending tool turn is process-local and must complete on the same client instance; completed ordinary
/// conversation turns remain durably resumable through <see cref="ChatOptions.ConversationId"/>.</para>
/// <para><b>Supported options.</b> <see cref="ChatOptions.ModelId"/>, <see cref="ChatOptions.Instructions"/>,
/// <see cref="ChatOptions.Reasoning"/> (effort), <see cref="ChatOptions.ResponseFormat"/> (JSON), automatic
/// or disabled tools, and image attachments are mapped. Required-tool modes throw because the Copilot SDK
/// exposes no equivalent. <b>Unsupported (no Copilot SDK equivalent):</b>
/// <see cref="ChatOptions.Temperature"/>, <see cref="ChatOptions.MaxOutputTokens"/>,
/// <see cref="ChatOptions.TopP"/>, <see cref="ChatOptions.TopK"/>, <see cref="ChatOptions.FrequencyPenalty"/>,
/// <see cref="ChatOptions.PresencePenalty"/>, <see cref="ChatOptions.StopSequences"/>, and
/// <see cref="ChatOptions.Seed"/> are ignored.</para>
/// <para><b>Concurrency.</b> The shared client is started once on first use. A single
/// <see cref="CopilotSdkChatClient"/> is intended to be driven by one logical caller sequence at a time
/// (which is how <c>FunctionInvokingChatClient</c> and typical chat loops use it). It deliberately holds no
/// locks; it retains only short-lived pending proxy sessions between a tool call and its result. Overlapping
/// concurrent calls on the same instance are not supported. Use separate instances (or serialize calls) for
/// concurrent conversations.</para>
/// </remarks>
public sealed class CopilotSdkChatClient : IChatClient, IAsyncDisposable
{
    private const string ProviderName = "github-copilot";
    private const string ExternalRequestIdKey = "copilot.request_id";

    private readonly CopilotSdkConfiguration _configuration;
    private readonly ICopilotBackend _backend;
    private readonly bool _ownsBackend;
    private readonly ChatClientMetadata _metadata;
    private readonly Dictionary<string, PendingToolSession> _pendingToolSessions =
        new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="CopilotSdkChatClient"/> class.</summary>
    /// <param name="configuration">The configuration for the underlying Copilot client.</param>
    public CopilotSdkChatClient(CopilotSdkConfiguration configuration)
        : this(configuration, CreateBackend(configuration), ownsBackend: true)
    {
    }

    private sealed record PendingToolSession(
        ICopilotSession Session,
        SessionEventRelay Relay,
        PendingToolCoordinator Coordinator);

    private sealed class SessionEventRelay(Action<SessionEvent> handler)
    {
        internal Action<SessionEvent>? Handler { get; set; } = handler;

        internal void Dispatch(SessionEvent evt) => Handler?.Invoke(evt);
    }

    // Test seam: allows a fake backend to drive the client without a live runtime.
    internal CopilotSdkChatClient(CopilotSdkConfiguration configuration, ICopilotBackend backend, bool ownsBackend)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(backend);
        configuration.Validate();

        _configuration = configuration;
        _backend = backend;
        _ownsBackend = ownsBackend;
        _metadata = new ChatClientMetadata(ProviderName, providerUri: null, defaultModelId: configuration.Model);
    }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken)
            .ConfigureAwait(false);

        // ToChatResponseAsync carries ConversationId, ModelId, FinishReason, ids, and usage across from the
        // updates; ensure a model id is always present when the runtime did not echo one.
        response.ModelId ??= options?.ModelId ?? _configuration.Model;
        return response;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one message must be supplied.", nameof(messages));
        }
        if (options?.ToolMode is RequiredChatToolMode)
        {
            throw new NotSupportedException(
                "The GitHub Copilot SDK does not expose a required-tool mode. " +
                "Use automatic tool selection or ChatToolMode.None.");
        }

        return StreamCoreAsync(list, options, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamCoreAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conversationId = options?.ConversationId;
        var hasTerminalToolResult = conversationId is not null
            && CopilotChatMapper.IsToolContinuation(messages);
        PendingToolSession? pendingSession = null;
        PendingToolSession? pendingCandidate = null;
        var coordinator = new PendingToolCoordinator();
        if (conversationId is not null
            && _pendingToolSessions.TryGetValue(
                conversationId,
                out pendingCandidate)
            && CopilotChatMapper.GetToolResults(messages).Any(
                result => pendingCandidate.Coordinator.IsPending(
                    result.CallId)))
        {
            _pendingToolSessions.Remove(conversationId);
            pendingSession = pendingCandidate;
            coordinator = pendingSession.Coordinator;
        }

        var tools = CopilotChatMapper.BuildTools(options, coordinator);
        var parameters = new CopilotSessionParameters
        {
            Model = options?.ModelId ?? _configuration.Model,
            ReasoningEffort = CopilotChatMapper.ResolveReasoningEffort(_configuration, options),
            SystemInstructions = CopilotChatMapper.BuildSystemInstructions(_configuration, options, messages),
            ToolDeclarations = tools.Declarations,
            AvailableTools = tools.AvailableTools,
            ExcludedTools = tools.ExcludedTools,
            WorkingDirectory = _configuration.WorkingDirectory,
            PermissionHandler = _configuration.PermissionHandler ?? CopilotSafePermissionPolicy.Create(tools.AllowedToolNames),
        };

        var channel = Channel.CreateUnbounded<SessionEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        void OnEvent(SessionEvent evt) => channel.Writer.TryWrite(evt);

        ICopilotSession? session = null;
        SessionEventRelay? relay = null;
        var completedNormally = false;
        try
        {
            if (pendingSession is not null)
            {
                session = pendingSession.Session;
                relay = pendingSession.Relay;
                relay.Handler = OnEvent;
                SubmitToolResults(coordinator, messages);
            }
            else if (conversationId is null)
            {
                relay = new SessionEventRelay(OnEvent);
                session = await _backend.CreateSessionAsync(
                    parameters,
                    relay.Dispatch,
                    cancellationToken).ConfigureAwait(false);
                await SendPromptAsync(session, CopilotChatMapper.BuildInitialPrompt(messages), messages, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (pendingCandidate is not null)
            {
                var suppliedCallIds = string.Join(
                    ", ",
                    CopilotChatMapper.GetToolResults(messages)
                        .Select(static result => result.CallId));
                throw new InvalidOperationException(
                    "The pending Copilot tool session did not receive a matching result. " +
                    $"Supplied CallIds: '{suppliedCallIds}'.");
            }
            else if (hasTerminalToolResult)
            {
                throw new InvalidOperationException(
                    "The pending Copilot tool session is no longer active. " +
                    "External tool continuations must complete on the same client instance.");
            }
            else
            {
                relay = new SessionEventRelay(OnEvent);
                session = await _backend
                    .ResumeSessionAsync(
                        conversationId,
                        parameters,
                        continuePendingWork: false,
                        relay.Dispatch,
                        cancellationToken)
                    .ConfigureAwait(false);
                await SendPromptAsync(session, CopilotChatMapper.BuildFollowUpPrompt(messages), messages, cancellationToken)
                    .ConfigureAwait(false);
            }

            var sessionId = session.SessionId;
            await foreach (var update in MapEventsAsync(
                channel.Reader,
                sessionId,
                parameters.Model,
                ignoreIdleUntilActivity: pendingSession is not null,
                cancellationToken)
                .ConfigureAwait(false))
            {
                if (update.FinishReason == ChatFinishReason.ToolCalls)
                {
                    if (session is null)
                    {
                        throw new InvalidOperationException(
                            "The Copilot runtime emitted more than one terminal tool boundary.");
                    }

                    _pendingToolSessions[sessionId] = new PendingToolSession(
                        session,
                        relay!,
                        coordinator);
                    session = null;
                    completedNormally = true;
                }
                else if (update.FinishReason is not null)
                {
                    completedNormally = true;
                }
                yield return update;
            }

            completedNormally = true;
        }
        finally
        {
            if (session is not null)
            {
                // On cancellation, timeout, or error, abort in-flight work so the runtime stops promptly.
                // On normal completion (idle or a tool-call boundary) do not abort — disposing the session
                // preserves durable state so the conversation can be resumed.
                if (!completedNormally)
                {
                    await session.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                }

                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async IAsyncEnumerable<ChatResponseUpdate> MapEventsAsync(
        ChannelReader<SessionEvent> reader,
        string conversationId,
        string? model,
        bool ignoreIdleUntilActivity,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var streamedMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var streamedReasoningIds = new HashSet<string>(StringComparer.Ordinal);
        var anyStreamedText = false;
        var observedContinuationActivity = !ignoreIdleUntilActivity;
        var responseModel = model;
        string? responseId = null;

        while (true)
        {
            SessionEvent evt = await ReadNextAsync(reader, cancellationToken).ConfigureAwait(false);
            if (IsContinuationActivity(evt))
            {
                observedContinuationActivity = true;
            }

            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                {
                    var messageId = delta.Data.MessageId;
                    if (!string.IsNullOrEmpty(messageId))
                    {
                        streamedMessageIds.Add(messageId);
                    }

                    if (!string.IsNullOrEmpty(delta.Data.DeltaContent))
                    {
                        anyStreamedText = true;
                        yield return CreateUpdate(
                            conversationId, responseModel, responseId, evt, ChatRole.Assistant,
                            [new TextContent(delta.Data.DeltaContent)], messageId: messageId);
                    }

                    break;
                }

                case AssistantReasoningDeltaEvent reasoning when !string.IsNullOrEmpty(reasoning.Data.DeltaContent):
                    if (!string.IsNullOrEmpty(reasoning.Data.ReasoningId))
                    {
                        streamedReasoningIds.Add(reasoning.Data.ReasoningId);
                    }
                    yield return CreateUpdate(
                        conversationId, responseModel, responseId, evt, ChatRole.Assistant,
                        [new TextReasoningContent(reasoning.Data.DeltaContent)]);
                    break;

                case AssistantReasoningEvent reasoning
                    when !streamedReasoningIds.Contains(reasoning.Data.ReasoningId)
                        && !string.IsNullOrEmpty(reasoning.Data.Content):
                    yield return CreateUpdate(
                        conversationId, responseModel, responseId, evt, ChatRole.Assistant,
                        [new TextReasoningContent(reasoning.Data.Content)]);
                    break;

                case AssistantMessageEvent message:
                {
                    responseModel = string.IsNullOrEmpty(message.Data.Model) ? responseModel : message.Data.Model;
                    responseId = string.IsNullOrEmpty(message.Data.MessageId) ? responseId : message.Data.MessageId;

                    // Emit the final text only when it was not already streamed via deltas (avoids duplication
                    // between streamed deltas and the final complete message). When the message carries an id,
                    // match on it; otherwise fall back to whether any delta text was streamed this turn.
                    var alreadyStreamed = !string.IsNullOrEmpty(message.Data.MessageId)
                        ? streamedMessageIds.Contains(message.Data.MessageId)
                        : anyStreamedText;
                    if (!alreadyStreamed && !string.IsNullOrEmpty(message.Data.Content))
                    {
                        yield return CreateUpdate(
                            conversationId, responseModel, responseId, evt, ChatRole.Assistant,
                            [new TextContent(message.Data.Content)], messageId: message.Data.MessageId);
                    }

                    break;
                }

                case AssistantUsageEvent usage when MapUsage(usage.Data) is { } details:
                    responseModel = string.IsNullOrEmpty(usage.Data.Model)
                        ? responseModel
                        : usage.Data.Model;
                    yield return CreateUpdate(
                        conversationId, responseModel, responseId, evt, role: null, [new UsageContent(details)]);
                    break;

                case ExternalToolRequestedEvent toolRequest:
                {
                    var calls = new List<AIContent>();
                    AddFunctionCall(calls, toolRequest);

                    // Drain any sibling tool requests emitted together (parallel tool calls) so the whole batch
                    // is surfaced in a single terminal update.
                    while (reader.TryPeek(out var buffered)
                        && buffered is ExternalToolRequestedEvent)
                    {
                        if (!reader.TryRead(out var siblingEvent)
                            || siblingEvent is not ExternalToolRequestedEvent sibling)
                        {
                            break;
                        }
                        AddFunctionCall(calls, sibling);
                    }

                    yield return CreateUpdate(
                        conversationId, responseModel, responseId, toolRequest, ChatRole.Assistant, calls,
                        finishReason: ChatFinishReason.ToolCalls);
                    yield break;
                }

                case SessionErrorEvent error:
                    throw new CopilotSdkException(
                        string.IsNullOrEmpty(error.Data.Message) ? "The Copilot runtime reported an error." : error.Data.Message,
                        error.Data.ErrorCode,
                        error.Data.ErrorType);

                case AbortEvent abort:
                    throw new OperationCanceledException(
                        $"The Copilot session was aborted ({abort.Data.Reason.Value}).");

                case SessionIdleEvent when !observedContinuationActivity:
                    // Resume can replay the idle state that existed while the external tool
                    // was pending. Wait for activity caused by the submitted result.
                    break;

                case SessionIdleEvent:
                    yield return CreateUpdate(
                        conversationId, responseModel, responseId, evt, role: null, [],
                        finishReason: ChatFinishReason.Stop);
                    yield break;

                default:
                    break;
            }
        }
    }

    private static bool IsContinuationActivity(SessionEvent evt) =>
        evt is AssistantMessageStartEvent
            or AssistantMessageDeltaEvent
            or AssistantMessageEvent
            or AssistantReasoningDeltaEvent
            or AssistantReasoningEvent
            or AssistantUsageEvent
            or ExternalToolRequestedEvent
            or ExternalToolCompletedEvent
            or ToolExecutionStartEvent
            or ToolExecutionCompleteEvent
            or SessionErrorEvent
            or AbortEvent;

    private async ValueTask<SessionEvent> ReadNextAsync(
        ChannelReader<SessionEvent> reader,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_configuration.StreamingInactivityTimeout);

        try
        {
            return await reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No response from the Copilot runtime within {_configuration.StreamingInactivityTimeout.TotalSeconds:0.###}s.");
        }
    }

    private static async Task SendPromptAsync(
        ICopilotSession session,
        string prompt,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var messageOptions = new MessageOptions { Prompt = prompt };
        var attachments = CopilotChatMapper.BuildAttachments(messages);
        if (attachments.Count > 0)
        {
            messageOptions.Attachments = [.. attachments];
        }

        await session.SendAsync(messageOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void SubmitToolResults(
        PendingToolCoordinator coordinator,
        IReadOnlyList<ChatMessage> messages)
    {
        var results = CopilotChatMapper.GetToolResults(messages);
        if (results.Count == 0)
        {
            return;
        }

        foreach (var result in results)
        {
            if (coordinator.IsPending(result.CallId))
                coordinator.SupplyResult(result);
        }
    }

    private static void AddFunctionCall(List<AIContent> target, ExternalToolRequestedEvent evt)
    {
        var call = new FunctionCallContent(
            evt.Data.ToolCallId ?? string.Empty,
            evt.Data.ToolName ?? string.Empty,
            ParseArguments(evt.Data.Arguments))
        {
            RawRepresentation = evt,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ExternalRequestIdKey] = evt.Data.RequestId,
            },
        };
        target.Add(call);
    }

    private static IDictionary<string, object?>? ParseArguments(JsonElement? arguments)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    private static UsageDetails? MapUsage(AssistantUsageData data)
    {
        UsageDetails? details = null;

        void Ensure() => details ??= new UsageDetails();

        if (data.InputTokens is { } input)
        {
            Ensure();
            details!.InputTokenCount = input;
        }

        if (data.OutputTokens is { } output)
        {
            Ensure();
            details!.OutputTokenCount = output;
        }

        if (data.CacheReadTokens is { } cacheRead)
        {
            Ensure();
            details!.CachedInputTokenCount = cacheRead;
        }

        if (data.ReasoningTokens is { } reasoning)
        {
            Ensure();
            details!.ReasoningTokenCount = reasoning;
        }

        if (details is not null && data.InputTokens is { } i && data.OutputTokens is { } o)
        {
            // Reasoning tokens are already included in OutputTokenCount by the M.E.AI contract.
            details.TotalTokenCount = i + o;
        }

        return details;
    }

    private static ChatResponseUpdate CreateUpdate(
        string conversationId,
        string? model,
        string? responseId,
        SessionEvent rawEvent,
        ChatRole? role,
        IList<AIContent> contents,
        ChatFinishReason? finishReason = null,
        string? messageId = null)
    {
        return new ChatResponseUpdate(role, contents)
        {
            ConversationId = conversationId,
            ModelId = model,
            ResponseId = responseId,
            MessageId = messageId,
            FinishReason = finishReason,
            RawRepresentation = rawEvent,
            CreatedAt = rawEvent.Timestamp,
        };
    }

    /// <summary>Lists the models advertised by the Copilot runtime.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The available models.</returns>
    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _backend.ListModelsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Permanently deletes a conversation (Copilot session) and its on-disk data. This is different from
    /// disposing a request session, which preserves durable state for later resumption.
    /// </summary>
    /// <param name="conversationId">The conversation id (Copilot session id) to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pendingToolSessions.Remove(conversationId, out var pending))
        {
            await pending.Session.DisposeAsync().ConfigureAwait(false);
        }
        await _backend.DeleteSessionAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return _metadata;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        if (serviceType == typeof(CopilotClient))
        {
            return _backend.GetUnderlyingClient();
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pending in _pendingToolSessions.Values)
        {
            pending.Session
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        _pendingToolSessions.Clear();
        if (_ownsBackend)
        {
            _backend.Dispose();
        }
    }

    /// <summary>Asynchronously releases the resources used by the client.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pending in _pendingToolSessions.Values)
        {
            await pending.Session.DisposeAsync().ConfigureAwait(false);
        }
        _pendingToolSessions.Clear();
        if (_ownsBackend)
        {
            await _backend.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ICopilotBackend CreateBackend(CopilotSdkConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new CopilotClientBackend(configuration);
    }
}
