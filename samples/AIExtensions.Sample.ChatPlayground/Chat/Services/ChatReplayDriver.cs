using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Replays the current recording through the same engine path as a live chat: each recorded user message is sent
/// again, and the replay client answers with the recorded response, so a replay renders exactly like the original.
/// </summary>
/// <remarks>
/// Call it on the thread that owns the session. Approvals and manual UI actions that a replayed turn waits for are
/// answered from the recorded request that continued the turn. Interactions that are not a user turn are restored as
/// history instead of being sent. When a recorded request continues a different conversation than the one shown, for
/// example because the recording starts part-way through a conversation or a turn was stopped before it was recorded,
/// that conversation is restored before its message is sent.
/// </remarks>
public sealed class ChatReplayDriver
{
    private readonly AgentContext _session;
    private readonly PlaygroundChatClient _router;
    private readonly ChatSessionService _recording;
    private ConversationTurn? _lastTurn;

    /// <summary>Initializes the driver for one session, router, and recording.</summary>
    public ChatReplayDriver(AgentContext session, PlaygroundChatClient router, ChatSessionService recording)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(recording);
        _session = session;
        _router = router;
        _recording = recording;
    }

    /// <summary>Raised before a recorded interaction is replayed or restored.</summary>
    public event EventHandler<RecordedInteraction>? InteractionStarting;

    /// <summary>Gets whether a replay is running.</summary>
    public bool IsReplaying { get; private set; }

    /// <summary>
    /// Replays the next turn, or every turn from the start. A replay that starts from the first turn clears the
    /// conversation first.
    /// </summary>
    /// <param name="playAll">Whether to restart and replay every turn.</param>
    /// <param name="cancellationToken">Stops the replay; the interrupted turn is not marked as replayed.</param>
    /// <returns>What the replay did.</returns>
    /// <exception cref="InvalidOperationException">A replayed turn failed.</exception>
    /// <exception cref="OperationCanceledException">
    /// The replay was stopped, by <paramref name="cancellationToken"/> or by stopping the session's response.
    /// </exception>
    public async Task<ChatReplayResult> ReplayAsync(bool playAll, CancellationToken cancellationToken = default)
    {
        if (IsReplaying)
            throw new InvalidOperationException("A replay is already running.");
        if (_recording.InteractionCount == 0)
            throw new InvalidOperationException("There is no chat to replay.");
        if (!playAll && !_recording.HasReplayRemaining)
            throw new InvalidOperationException("No more turns to replay. Press Play all to start again.");

        IsReplaying = true;
        _router.IsReplaying = true;
        var result = new ChatReplayResult();
        try
        {
            if (playAll)
                _recording.RestartReplay();
            if (playAll || _recording.ReplayPosition == 0)
            {
                await _session.ClearAsync();
                _lastTurn = null;
            }

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReplayTurnAsync(_recording.PeekNext(), result, cancellationToken);
            }
            while (playAll && _recording.HasReplayRemaining);

            return result;
        }
        finally
        {
            _router.IsReplaying = false;
            IsReplaying = false;
        }
    }

    private async Task ReplayTurnAsync(
        RecordedInteraction interaction, ChatReplayResult result, CancellationToken cancellationToken)
    {
        var request = ReadConversation(interaction);
        InteractionStarting?.Invoke(this, interaction);
        if (request.Count == 0 || !IsUserTurn(request[^1]))
        {
            // The recorded request holds the whole conversation so far, so it replaces what is shown.
            await ShowConversationAsync([.. request, .. ReadResponse(interaction).Messages], result, cancellationToken);
            _recording.CompleteReplay(interaction);
            result.RestoredInteractions++;
            return;
        }

        var history = request.Take(request.Count - 1).ToList();
        if (!ContinuesShownConversation(history))
        {
            await ShowConversationAsync(history, result, cancellationToken);
            result.RestoredHistory++;
        }

        var waiting = new List<InteractiveFunctionBlock>();
        var stoppedByReplay = false;
        using var blockAdded = _session.RegisterOnBlockAdded((_, block) =>
        {
            if (block is ToolApprovalBlock or UIActionBlock { Mode: UIActionInvocationMode.Manual })
                waiting.Add((InteractiveFunctionBlock)block);
        });
        using var statusChanged = _session.RegisterOnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput || AnswerFromRecording(waiting, result))
                return;

            // Waiting for input the recording cannot supply would never end, so the turn is stopped, as it was when it
            // was recorded.
            stoppedByReplay = true;
            _ = _session.CancelAsync();
        });

        await _session.SendMessageAsync(request[^1], cancellationToken);
        var turn = _session.Turns.Count > 0 ? _session.Turns[^1] : null;
        if (_session.Status == ConversationStatus.Error)
        {
            var error = _session.Error;
            throw new InvalidOperationException(error?.Message ?? "The replayed turn failed.", error);
        }

        // Stop in the chat view stops the session's response, not the replay. The replay client did not finish the
        // interaction, so carrying on would send it again; end the replay as Stop replay does instead.
        if (turn is { IsStopped: true } && !stoppedByReplay)
            throw new OperationCanceledException("The replay was stopped.");

        // Only a turn that played as recorded continues the recording. A failed or stopped one is replaced when its
        // interaction is replayed again.
        _lastTurn = turn;
        result.Turns++;
    }

    /// <summary>
    /// Answers what the turn waits for from the recorded request that continued the turn: the next one, when it ends
    /// with approval answers or action results rather than with a new user message. Approvals are decided as recorded,
    /// and manual UI actions run with the arguments their recorded call had when it ran.
    /// </summary>
    /// <returns>Whether everything the turn waits for was answered.</returns>
    private bool AnswerFromRecording(List<InteractiveFunctionBlock> waiting, ChatReplayResult result)
    {
        // A turn that was stopped after its approvals were answered was never recorded past them, but the requests
        // after it still carry those answers in their history. Only answers that end a request continued this turn.
        var request = _recording.HasReplayRemaining ? ReadConversation(_recording.PeekNext()) : [];
        var continuation = ReadContinuation(request);

        var answered = 0;
        var unanswered = false;
        foreach (var block in waiting)
        {
            switch (block)
            {
                case ToolApprovalBlock { Status: ApprovalStatus.Pending } approval:
                    var answer = continuation.OfType<ToolApprovalResponseContent>().FirstOrDefault(response =>
                        string.Equals(response.RequestId, approval.ApprovalRequest.RequestId, StringComparison.Ordinal));
                    if (answer is null)
                    {
                        unanswered = true;
                        result.UnansweredApprovals.Add(approval.ToolName ?? "a tool");
                        continue;
                    }

                    answered++;
                    if (answer.Approved)
                        approval.Approve();
                    else
                        approval.Reject(answer.Reason);
                    break;

                case UIActionBlock { IsAwaitingInput: true, Call: { } call } action:
                    if (!continuation.OfType<FunctionResultContent>().Any(ran =>
                        string.Equals(ran.CallId, call.CallId, StringComparison.Ordinal)))
                    {
                        unanswered = true;
                        result.UnansweredApprovals.Add(action.ToolName ?? "an action");
                        continue;
                    }

                    // What the user supplied, such as a decision that a custom card added to the call's arguments, is
                    // in the recorded call that the continuation sent with the action's result.
                    if (FindCall(request, call.CallId)?.Arguments is { } recordedArguments)
                    {
                        call.Arguments ??= new Dictionary<string, object?>();
                        foreach (var (name, value) in recordedArguments)
                            call.Arguments[name] = value;
                    }

                    answered++;
                    _ = action.InvokeAsync();
                    break;
            }
        }

        if (!unanswered && answered == 0)
        {
            result.UnansweredApprovals.Add("an input request");
            return false;
        }

        return !unanswered;
    }

    /// <summary>Reads the contents of the approval answers and tool results that end a recorded request.</summary>
    private static List<AIContent> ReadContinuation(IReadOnlyList<ChatMessage> request)
    {
        var contents = new List<AIContent>();
        for (var index = request.Count - 1; index >= 0 && PlaygroundChatClient.IsContinuation(request[index]); index--)
            contents.AddRange(request[index].Contents);
        return contents;
    }

    private static FunctionCallContent? FindCall(IReadOnlyList<ChatMessage> request, string callId) =>
        request.SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .LastOrDefault(call => string.Equals(call.CallId, callId, StringComparison.Ordinal));

    /// <summary>
    /// Gets whether the conversation shown is the one a recorded request continues: the previous interaction's request
    /// and response, when the replay produced everything shown.
    /// </summary>
    private bool ContinuesShownConversation(IReadOnlyList<ChatMessage> history)
    {
        IEnumerable<ChatMessage> shown;
        if (_session.Turns.Count == 0)
        {
            shown = [];
        }
        else if (_lastTurn is null || !ReferenceEquals(_session.Turns[^1], _lastTurn) ||
            _recording.PeekPrevious() is not { } previous)
        {
            // The chat continued without the replay, for example live after an earlier replay.
            return false;
        }
        else
        {
            shown = [.. ReadConversation(previous), .. ReadResponse(previous).Messages];
        }

        return DescribeConversation(shown).SequenceEqual(DescribeConversation(history), StringComparer.Ordinal);
    }

    private async Task ShowConversationAsync(
        IReadOnlyList<ChatMessage> messages, ChatReplayResult result, CancellationToken cancellationToken)
    {
        await _session.ClearAsync();
        if (messages.Count > 0)
            await _session.RestoreAsync(messages, cancellationToken);
        _lastTurn = _session.Turns.Count > 0 ? _session.Turns[^1] : null;

        // The turns the replay stopped are no longer shown.
        result.UnansweredApprovals.Clear();
    }

    /// <summary>
    /// Reads the conversation a recorded request carried, without the messages added to that request only, such as a
    /// scenario's state: they were never part of the conversation, and the next request carries its own.
    /// </summary>
    private static List<ChatMessage> ReadConversation(RecordedInteraction interaction) =>
        [.. ChatRecordingSerializer.ReadRequestMessages(interaction.Request)
            .Where(static message => !PlaygroundChatClient.IsRequestContext(message))];

    private static ChatResponse ReadResponse(RecordedInteraction interaction) =>
        interaction.IsStreaming
            ? interaction.Updates.Select(ChatRecordingSerializer.ReadUpdate).ToChatResponse()
            : ChatRecordingSerializer.ReadResponse(
                interaction.Response ?? throw new InvalidDataException("The non-streaming recording has no response."));

    /// <summary>Gets whether a recorded request ends with a message that the user typed or attached.</summary>
    internal static bool IsUserTurn(ChatMessage message) =>
        message.Role == ChatRole.User &&
        message.Contents.Count > 0 &&
        message.Contents.All(static content => content is TextContent or DataContent or UriContent);

    /// <summary>
    /// Describes what a conversation says, however its messages are grouped, so the conversation that a recorded
    /// request continues can be compared with the one the replay shows.
    /// </summary>
    /// <remarks>
    /// Text and reasoning are joined per speaker, and a call or result that a stream repeats counts once. Usage,
    /// approvals, and calls that were never answered are left out: the engine keeps no unanswered calls, and an
    /// answered approval shows as its call and result.
    /// </remarks>
    internal static IReadOnlyList<string> DescribeConversation(IEnumerable<ChatMessage> messages)
    {
        var conversation = messages.ToList();
        var answeredCalls = conversation
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(static result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);
        var parts = new List<string>();
        var counted = new HashSet<string>(StringComparer.Ordinal);
        var text = new StringBuilder();
        string? textKind = null;

        foreach (var message in conversation)
        {
            var speaker = message.Role == ChatRole.User ? "user"
                : message.Role == ChatRole.System ? "system"
                : "model";
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } textContent:
                        AddText($"{speaker} text", textContent.Text);
                        break;
                    case TextReasoningContent { Text.Length: > 0 } reasoning:
                        AddText($"{speaker} reasoning", reasoning.Text);
                        break;
                    case FunctionCallContent call when answeredCalls.Contains(call.CallId):
                        AddOnce($"call {call.CallId} {call.Name}");
                        break;
                    case FunctionResultContent result:
                        AddOnce($"result {result.CallId}");
                        break;
                    case ImageGenerationToolCallContent imageCall:
                        AddOnce($"image call {imageCall.CallId}");
                        break;
                    case ImageGenerationToolResultContent imageResult:
                        AddOnce($"image result {imageResult.CallId}");
                        break;
                    case DataContent data:
                        Add($"{speaker} data {data.MediaType} {data.Data.Length}");
                        break;
                    case UriContent uri:
                        Add($"{speaker} uri {uri.Uri}");
                        break;
                }
            }
        }

        CloseText();
        return parts;

        void AddText(string kind, string value)
        {
            if (textKind != kind)
            {
                CloseText();
                textKind = kind;
            }

            text.Append(value);
        }

        void Add(string part)
        {
            CloseText();
            parts.Add(part);
        }

        void AddOnce(string part)
        {
            if (counted.Add(part))
                Add(part);
        }

        void CloseText()
        {
            if (textKind is null)
                return;

            parts.Add($"{textKind}: {text}");
            text.Clear();
            textKind = null;
        }
    }
}

/// <summary>What a replay did.</summary>
public sealed class ChatReplayResult
{
    /// <summary>Gets the number of user turns that were sent again.</summary>
    public int Turns { get; internal set; }

    /// <summary>
    /// Gets the number of recorded interactions that were not a user turn and were restored as history instead.
    /// </summary>
    public int RestoredInteractions { get; internal set; }

    /// <summary>
    /// Gets how many times the conversation a recorded request continued was restored before its message was sent,
    /// because the conversation shown differed: the recording starts part-way through a conversation, a turn was
    /// stopped before it was recorded, or the chat continued live after an earlier replay.
    /// </summary>
    public int RestoredHistory { get; internal set; }

    /// <summary>
    /// Gets the tools whose approval, or the manual UI actions whose run, the recording never answered; their turns are
    /// shown stopped.
    /// </summary>
    public List<string> UnansweredApprovals { get; } = [];
}
