using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Replays the current recording through the same engine path as a live chat: each recorded user message is sent
/// again, and the replay client answers with the recorded response, so a replay renders exactly like the original.
/// </summary>
/// <remarks>
/// Call it on the thread that owns the session. Approvals that a replayed turn asks for are answered from the next
/// recorded request. Interactions that are not a user turn are restored as history instead of being sent, and so is
/// the earlier conversation of a recording that starts part-way through one.
/// </remarks>
public sealed class ChatReplayDriver
{
    private readonly AgentContext _session;
    private readonly PlaygroundChatClient _router;
    private readonly ChatSessionService _recording;

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
                await _session.ClearAsync();

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
        var request = ChatRecordingSerializer.ReadRequestMessages(interaction.Request);
        InteractionStarting?.Invoke(this, interaction);
        if (request.Count == 0 || !IsUserTurn(request[^1]))
        {
            await RestoreAsync(interaction, request, cancellationToken);
            result.RestoredInteractions++;
            return;
        }

        // A recording can start part-way through a conversation; show what came before its first message.
        if (_session.Turns.Count == 0 && request.Take(request.Count - 1).Any(static message => message.Role != ChatRole.System))
            await _session.RestoreAsync(request.Take(request.Count - 1), cancellationToken);

        var approvals = new List<ToolApprovalBlock>();
        using var blockAdded = _session.RegisterOnBlockAdded((_, block) =>
        {
            if (block is ToolApprovalBlock approval)
                approvals.Add(approval);
        });
        using var statusChanged = _session.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                AnswerFromRecording(approvals, result);
        });

        await _session.SendMessageAsync(request[^1], cancellationToken);
        if (_session.Status == ConversationStatus.Error)
        {
            var error = _session.Error;
            throw new InvalidOperationException(error?.Message ?? "The replayed turn failed.", error);
        }

        result.Turns++;
    }

    /// <summary>
    /// Answers the turn's open approvals from the next recorded request, which continued the turn when it was
    /// recorded. A turn that was left waiting (no recorded answer, or input the recording cannot supply) is stopped,
    /// as it was when it was recorded.
    /// </summary>
    private void AnswerFromRecording(List<ToolApprovalBlock> approvals, ChatReplayResult result)
    {
        var answers = _recording.HasReplayRemaining
            ? ChatRecordingSerializer.ReadRequestMessages(_recording.PeekNext().Request)
                .SelectMany(static message => message.Contents)
                .OfType<ToolApprovalResponseContent>()
                .ToList()
            : [];

        var answered = 0;
        var unanswered = false;
        foreach (var approval in approvals)
        {
            if (approval.Status != ApprovalStatus.Pending)
                continue;

            var answer = answers.FirstOrDefault(response =>
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
        }

        // Waiting for anything else would never end, so a replay never leaves a turn waiting for input.
        if (unanswered || answered == 0)
        {
            if (!unanswered)
                result.UnansweredApprovals.Add("an input request");
            _ = _session.CancelAsync();
        }
    }

    private async Task RestoreAsync(
        RecordedInteraction interaction, IReadOnlyList<ChatMessage> request, CancellationToken cancellationToken)
    {
        var response = interaction.IsStreaming
            ? interaction.Updates.Select(ChatRecordingSerializer.ReadUpdate).ToChatResponse()
            : ChatRecordingSerializer.ReadResponse(
                interaction.Response ?? throw new InvalidDataException("The non-streaming recording has no response."));

        // The recorded request holds the whole conversation so far, so it replaces what is shown.
        await _session.ClearAsync();
        await _session.RestoreAsync([.. request, .. response.Messages], cancellationToken);
        _recording.CompleteReplay(interaction);
    }

    /// <summary>Gets whether a recorded request ends with a message that the user typed or attached.</summary>
    internal static bool IsUserTurn(ChatMessage message) =>
        message.Role == ChatRole.User &&
        message.Contents.Count > 0 &&
        message.Contents.All(static content => content is TextContent or DataContent or UriContent);
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

    /// <summary>Gets the tools whose approval the recording never answered; their turns were stopped.</summary>
    public List<string> UnansweredApprovals { get; } = [];
}
