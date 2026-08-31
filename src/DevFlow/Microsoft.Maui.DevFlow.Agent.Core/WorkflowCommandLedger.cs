using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Agent.Core.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Keeps the in-process receipt ledger for broker-owned workflow mutations. The ledger is
/// deliberately bound to the broker-assigned agent instance and is not a recovery mechanism
/// across an app-process restart.
/// </summary>
internal sealed class WorkflowCommandLedger
{
    private readonly object _gate = new();
    private readonly Func<string?> _getAgentInstanceId;
    private readonly Func<bool> _hasBrokerAuthority;
    private readonly WorkflowCommandLedgerOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<WorkflowLedgerKey, WorkflowRunLedger> _runs = new();

    public WorkflowCommandLedger(
        Func<string?> getAgentInstanceId,
        Func<bool> hasBrokerAuthority,
        WorkflowCommandLedgerOptions? options = null,
        TimeProvider? clock = null)
    {
        _getAgentInstanceId = getAgentInstanceId ?? throw new ArgumentNullException(nameof(getAgentInstanceId));
        _hasBrokerAuthority = hasBrokerAuthority ?? throw new ArgumentNullException(nameof(hasBrokerAuthority));
        _options = options ?? new WorkflowCommandLedgerOptions();
        _clock = clock ?? TimeProvider.System;

        if (_options.MaxActiveLedgers < 1 ||
            _options.MaxCommandsPerRun < 1 ||
            _options.MaxRetainedTerminalLedgers < 1 ||
            _options.MaxStoredResponseBytes < 1 ||
            _options.TerminalRetention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Workflow command ledger limits are invalid.");
        }
    }

    internal int ActiveLedgerCount
    {
        get
        {
            lock (_gate)
                return _runs.Values.Count(static run => run.IsActive);
        }
    }

    internal int RetainedLedgerCount
    {
        get
        {
            lock (_gate)
                return _runs.Values.Count(static run => !run.IsActive);
        }
    }

    public WorkflowRunControlStatus Control(WorkflowRunControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var action = request.Action?.Trim().ToLowerInvariant();
        if (action is not ("begin" or "end" or "abandon"))
            return WorkflowRunControlStatus.Failure(400, "workflow-control", "action must be begin, end, or abandon.");

        if (!TryValidateControl(request, out var key, out var currentInstance, out var error))
            return error!;

        lock (_gate)
        {
            ReconcileAgentInstanceLocked(currentInstance);
            CleanupTerminalLedgersLocked();

            if (action == "begin")
            {
                if (!_hasBrokerAuthority())
                {
                    return WorkflowRunControlStatus.Failure(
                        503,
                        "workflow-broker-unavailable",
                        "Workflow runs require active broker authority.");
                }

                if (_runs.TryGetValue(key, out var existing))
                {
                    if (existing.IsActive &&
                        existing.AuthorityEpoch == request.AuthorityEpoch &&
                        string.Equals(existing.ApprovalDigest, NormalizeOptional(request.ApprovalDigest), StringComparison.Ordinal))
                    {
                        return WorkflowRunControlStatus.Success(existing, alreadyTerminal: false);
                    }

                    return WorkflowRunControlStatus.Failure(
                        409,
                        existing.IsActive ? "workflow-run-conflict" : "workflow-run-terminal",
                        existing.IsActive
                            ? "A workflow run with this ID is already active."
                            : "A workflow run with this ID is already terminal.");
                }

                if (_runs.Values.Count(static run => run.IsActive) >= _options.MaxActiveLedgers)
                {
                    return WorkflowRunControlStatus.Failure(
                        429,
                        "workflow-active-capacity",
                        "The agent has reached its active workflow run limit.");
                }

                var run = new WorkflowRunLedger(
                    key,
                    request.AuthorityEpoch,
                    NormalizeOptional(request.ApprovalDigest),
                    _clock.GetUtcNow());
                _runs.Add(key, run);
                return WorkflowRunControlStatus.Success(run, alreadyTerminal: false);
            }

            if (!_runs.TryGetValue(key, out var target))
            {
                return WorkflowRunControlStatus.Failure(
                    409,
                    "workflow-run",
                    "The workflow run is not active on this agent instance.");
            }

            if (target.AuthorityEpoch != request.AuthorityEpoch)
            {
                return WorkflowRunControlStatus.Failure(
                    409,
                    "workflow-stale-epoch",
                    "The workflow authority epoch is stale.");
            }

            if (!string.Equals(target.ApprovalDigest, NormalizeOptional(request.ApprovalDigest), StringComparison.Ordinal))
            {
                return WorkflowRunControlStatus.Failure(
                    409,
                    "workflow-approval-digest",
                    "The workflow approval digest does not match the active run.");
            }

            if (!target.IsActive)
                return WorkflowRunControlStatus.Success(target, alreadyTerminal: true);

            if (action == "end")
            {
                if (!_hasBrokerAuthority())
                {
                    return WorkflowRunControlStatus.Failure(
                        503,
                        "workflow-broker-unavailable",
                        "Workflow runs require active broker authority.");
                }

                var incompleteCommands = target.Commands.Values
                    .Where(static command => command.State is WorkflowCommandState.Prepared or WorkflowCommandState.Dispatched or WorkflowCommandState.UnknownCompletion)
                    .ToArray();
                if (incompleteCommands.Length > 0)
                {
                    foreach (var command in incompleteCommands)
                    {
                        command.State = WorkflowCommandState.UnknownCompletion;
                        command.Completion.TrySetResult(StoredHttpResponse.UnknownCompletion());
                    }
                    MarkTerminalLocked(target, "unknown-completion");
                    return WorkflowRunControlStatus.Failure(
                        409,
                        "workflow-unknown-completion",
                        "The workflow has a command with unknown completion.");
                }

                MarkTerminalLocked(target, "completed");
            }
            else
            {
                var incompleteCommands = target.Commands.Values
                    .Where(static command => command.State is WorkflowCommandState.Prepared or WorkflowCommandState.Dispatched or WorkflowCommandState.UnknownCompletion)
                    .ToArray();
                if (incompleteCommands.Length > 0)
                {
                    foreach (var command in incompleteCommands)
                    {
                        command.State = WorkflowCommandState.UnknownCompletion;
                        command.Completion.TrySetResult(StoredHttpResponse.UnknownCompletion());
                    }
                    MarkTerminalLocked(target, "unknown-completion");
                }
                else
                {
                    MarkTerminalLocked(target, NormalizeTerminalReason(request.Reason));
                }
            }

            CleanupTerminalLedgersLocked();
            return WorkflowRunControlStatus.Success(target, alreadyTerminal: false);
        }
    }

    public async Task<WorkflowCommandPreparation> PrepareAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!WorkflowCommandHeaders.HasAny(request))
            return WorkflowCommandPreparation.None;

        if (!WorkflowCommandHeaders.TryRead(request, out var envelope, out var error))
            return WorkflowCommandPreparation.FromResponse(error!);

        var actualDigest = WorkflowCommandDigest.Compute(
            request.Method,
            WorkflowCommandDigest.CanonicalPath(request),
            request.Body);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(actualDigest),
                Encoding.UTF8.GetBytes(envelope.ActionDigest)))
        {
            return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                "The workflow command digest does not match this request.",
                409,
                "workflow-action-digest"));
        }

        Task<StoredHttpResponse>? pendingCompletion = null;
        lock (_gate)
        {
            var currentInstance = _getAgentInstanceId();
            ReconcileAgentInstanceLocked(currentInstance);
            CleanupTerminalLedgersLocked();

            if (!_hasBrokerAuthority())
            {
                MarkBrokerUnavailableLocked(envelope);
                return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                    "Workflow commands require active broker authority.",
                    503,
                    "workflow-broker-unavailable"));
            }

            if (string.IsNullOrWhiteSpace(currentInstance) ||
                !string.Equals(currentInstance, envelope.AgentInstanceId, StringComparison.Ordinal))
            {
                return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                    "The workflow command targets a different agent instance.",
                    409,
                    "workflow-agent-instance"));
            }

            var key = new WorkflowLedgerKey(envelope.AgentInstanceId, envelope.RunId);
            if (!_runs.TryGetValue(key, out var run))
            {
                return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                    "The workflow run is not active on this agent instance.",
                    409,
                    "workflow-run"));
            }

            if (run.AuthorityEpoch != envelope.AuthorityEpoch)
            {
                return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                    "The workflow authority epoch is stale.",
                    409,
                    "workflow-stale-epoch"));
            }

            if (!string.Equals(run.ApprovalDigest, envelope.ApprovalDigest, StringComparison.Ordinal))
            {
                return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                    "The workflow approval digest does not match the active run.",
                    409,
                    "workflow-approval-digest"));
            }

            if (run.Commands.TryGetValue(envelope.CommandId, out var existing))
            {
                if (existing.Sequence != envelope.Sequence ||
                    !string.Equals(existing.ActionDigest, envelope.ActionDigest, StringComparison.Ordinal) ||
                    existing.AuthorityEpoch != envelope.AuthorityEpoch ||
                    !string.Equals(existing.ApprovalDigest, envelope.ApprovalDigest, StringComparison.Ordinal))
                {
                    return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                        "The workflow command ID was reused with different command details.",
                        409,
                        "workflow-command-conflict"));
                }

                if (existing.State == WorkflowCommandState.Completed)
                    return WorkflowCommandPreparation.FromResponse(existing.Response!.ToHttpResponse());

                if (existing.State == WorkflowCommandState.UnknownCompletion)
                {
                    return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                        "The workflow command has unknown completion and cannot be dispatched again.",
                        409,
                        "workflow-unknown-completion"));
                }

                if (!run.IsActive)
                {
                    return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                        "The workflow run is terminal and the command did not produce a receipt.",
                        409,
                        "workflow-unknown-completion"));
                }

                pendingCompletion = existing.Completion.Task;
            }
            else
            {
                if (!run.IsActive)
                {
                    return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                        run.TerminalReason == "unknown-completion"
                            ? "A previous workflow command has unknown completion."
                            : "The workflow run is terminal.",
                        409,
                        run.TerminalReason == "unknown-completion"
                            ? "workflow-unknown-completion"
                            : "workflow-run-terminal"));
                }

                if (run.Commands.Count >= _options.MaxCommandsPerRun)
                {
                    return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                        "The workflow run has reached its command limit.",
                        429,
                        "workflow-command-capacity"));
                }

                var expectedSequence = run.LastSequence + 1;
                if (envelope.Sequence != expectedSequence)
                {
                    return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                        envelope.Sequence < expectedSequence
                            ? "Workflow command sequence regressed."
                            : "Workflow command sequence is not contiguous.",
                        409,
                        envelope.Sequence < expectedSequence
                            ? "workflow-sequence-regression"
                            : "workflow-sequence-gap",
                        new { expectedSequence, receivedSequence = envelope.Sequence }));
                }

                var command = new WorkflowCommandEntry(envelope, _clock.GetUtcNow());
                run.Commands.Add(envelope.CommandId, command);
                run.LastSequence = envelope.Sequence;
                command.State = WorkflowCommandState.Dispatched;
                command.DispatchedAt = _clock.GetUtcNow();
                return WorkflowCommandPreparation.Execute(new WorkflowCommandReservation(run.Key, envelope.CommandId));
            }
        }

        try
        {
            var completed = await pendingCompletion!.WaitAsync(cancellationToken).ConfigureAwait(false);
            return WorkflowCommandPreparation.FromResponse(completed.ToHttpResponse());
        }
        catch (OperationCanceledException)
        {
            MarkUnknownCompletion(envelope);
            return WorkflowCommandPreparation.FromResponse(HttpResponse.Error(
                "The workflow command completion is unknown.",
                409,
                "workflow-unknown-completion"));
        }
    }

    public HttpResponse Complete(WorkflowCommandReservation reservation, HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        lock (_gate)
        {
            if (!_runs.TryGetValue(reservation.RunKey, out var run) ||
                !run.Commands.TryGetValue(reservation.CommandId, out var command))
            {
                return HttpResponse.Error(
                    "The workflow command is no longer active.",
                    409,
                    "workflow-command");
            }

            if (command.State == WorkflowCommandState.Completed)
                return command.Response!.ToHttpResponse();

            if (command.State == WorkflowCommandState.UnknownCompletion)
            {
                return HttpResponse.Error(
                    "The workflow command completion is unknown.",
                    409,
                    "workflow-unknown-completion");
            }

            var stored = StoreResponse(response);
            command.Response = stored;
            command.CompletedAt = _clock.GetUtcNow();
            command.State = WorkflowCommandState.Completed;
            command.Completion.TrySetResult(stored);
            return stored.ToHttpResponse();
        }
    }

    public void MarkUnknownCompletion(WorkflowCommandReservation reservation)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(reservation.RunKey, out var run) ||
                !run.Commands.TryGetValue(reservation.CommandId, out var command) ||
                command.State is WorkflowCommandState.Completed or WorkflowCommandState.UnknownCompletion)
            {
                return;
            }

            command.State = WorkflowCommandState.UnknownCompletion;
            command.Completion.TrySetResult(StoredHttpResponse.UnknownCompletion());
            MarkTerminalLocked(run, "unknown-completion");
            CleanupTerminalLedgersLocked();
        }
    }

    private void MarkUnknownCompletion(WorkflowCommandEnvelope envelope)
    {
        MarkUnknownCompletion(new WorkflowCommandReservation(
            new WorkflowLedgerKey(envelope.AgentInstanceId, envelope.RunId),
            envelope.CommandId));
    }

    private bool TryValidateControl(
        WorkflowRunControlRequest request,
        out WorkflowLedgerKey key,
        out string? currentInstance,
        out WorkflowRunControlStatus? error)
    {
        key = default;
        currentInstance = _getAgentInstanceId();
        error = null;

        if (!IsSafeIdentifier(request.RunId, 128) ||
            !IsSafeIdentifier(request.AgentInstanceId, 128) ||
            request.AuthorityEpoch <= 0)
        {
            error = WorkflowRunControlStatus.Failure(
                400,
                "workflow-control",
                "runId, agentInstanceId, and a positive authorityEpoch are required.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ApprovalDigest) &&
            !IsSafeIdentifier(request.ApprovalDigest, 128))
        {
            error = WorkflowRunControlStatus.Failure(
                400,
                "workflow-control",
                "approvalDigest is invalid.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentInstance) ||
            !string.Equals(currentInstance, request.AgentInstanceId, StringComparison.Ordinal))
        {
            error = WorkflowRunControlStatus.Failure(
                409,
                "workflow-agent-instance",
                "The workflow control targets a different agent instance.");
            return false;
        }

        key = new WorkflowLedgerKey(request.AgentInstanceId!, request.RunId!);
        return true;
    }

    private void MarkBrokerUnavailableLocked(WorkflowCommandEnvelope envelope)
    {
        var key = new WorkflowLedgerKey(envelope.AgentInstanceId, envelope.RunId);
        if (_runs.TryGetValue(key, out var run) && run.IsActive)
            MarkTerminalLocked(run, "orphaned");
    }

    private void ReconcileAgentInstanceLocked(string? currentInstance)
    {
        if (string.IsNullOrWhiteSpace(currentInstance))
            return;

        foreach (var run in _runs.Values.Where(static candidate => candidate.IsActive).ToArray())
        {
            if (!string.Equals(run.Key.AgentInstanceId, currentInstance, StringComparison.Ordinal))
                MarkTerminalLocked(run, "orphaned");
        }
    }

    private void MarkTerminalLocked(WorkflowRunLedger run, string reason)
    {
        if (!run.IsActive)
            return;

        run.TerminalReason = reason;
        run.TerminalAt = _clock.GetUtcNow();
    }

    private void CleanupTerminalLedgersLocked()
    {
        var now = _clock.GetUtcNow();
        foreach (var key in _runs
            .Where(pair => !pair.Value.IsActive &&
                pair.Value.TerminalAt is { } terminalAt &&
                now - terminalAt >= _options.TerminalRetention)
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _runs.Remove(key);
        }

        while (_runs.Values.Count(static run => !run.IsActive) > _options.MaxRetainedTerminalLedgers)
        {
            var oldest = _runs
                .Where(static pair => !pair.Value.IsActive)
                .OrderBy(static pair => pair.Value.TerminalAt)
                .ThenBy(static pair => pair.Key.AgentInstanceId, StringComparer.Ordinal)
                .ThenBy(static pair => pair.Key.RunId, StringComparer.Ordinal)
                .Select(static pair => pair.Key)
                .FirstOrDefault();
            if (oldest == default)
                return;
            _runs.Remove(oldest);
        }
    }

    private StoredHttpResponse StoreResponse(HttpResponse response)
    {
        var bytes = response.BodyBytes ?? (response.Body is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(response.Body));
        if (bytes.Length > _options.MaxStoredResponseBytes)
        {
            return StoredHttpResponse.From(BoundedFailureResponse(
                "The workflow command response exceeded the receipt size limit.",
                "workflow-response-too-large"));
        }

        if (response.BodyBytes is not null &&
            response.Body is null &&
            !response.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
            !response.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return StoredHttpResponse.From(BoundedFailureResponse(
                "The workflow command response was binary and was not retained.",
                "workflow-response-sensitive"));
        }

        var text = response.Body ??
            (response.BodyBytes is not null ? Encoding.UTF8.GetString(response.BodyBytes) : null);
        if (text is { Length: > 0 } &&
            !string.Equals(DiagnosticRedactor.RedactText(text), text, StringComparison.Ordinal))
        {
            return StoredHttpResponse.From(BoundedFailureResponse(
                "The workflow command response contained sensitive content and was not retained.",
                "workflow-response-sensitive"));
        }

        return StoredHttpResponse.From(response);
    }

    private HttpResponse BoundedFailureResponse(string message, string reason)
    {
        var response = HttpResponse.Error(message, 500, reason);
        if (response.Body is null || Encoding.UTF8.GetByteCount(response.Body) <= _options.MaxStoredResponseBytes)
            return response;

        return new HttpResponse
        {
            StatusCode = 500,
            StatusText = reason,
            ContentType = "application/json"
        };
    }

    private static string NormalizeTerminalReason(string? reason)
        => reason?.Trim().ToLowerInvariant() switch
        {
            "cancelled" => "cancelled",
            "timed-out" => "timed-out",
            "unknown-completion" => "unknown-completion",
            "orphaned" => "orphaned",
            _ => "abandoned"
        };

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSafeIdentifier(string? value, int maximumLength)
        => value is { Length: > 0 } &&
           value.Length <= maximumLength &&
           value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    internal sealed class WorkflowRunLedger
    {
        public WorkflowRunLedger(
            WorkflowLedgerKey key,
            long authorityEpoch,
            string? approvalDigest,
            DateTimeOffset createdAt)
        {
            Key = key;
            AuthorityEpoch = authorityEpoch;
            ApprovalDigest = approvalDigest;
            CreatedAt = createdAt;
        }

        internal WorkflowLedgerKey Key { get; }
        internal long AuthorityEpoch { get; }
        internal string? ApprovalDigest { get; }
        internal DateTimeOffset CreatedAt { get; }
        internal Dictionary<string, WorkflowCommandEntry> Commands { get; } = new(StringComparer.Ordinal);
        internal long LastSequence { get; set; }
        internal string? TerminalReason { get; set; }
        internal DateTimeOffset? TerminalAt { get; set; }
        internal bool IsActive => TerminalReason is null;
    }

    internal sealed class WorkflowCommandEntry
    {
        public WorkflowCommandEntry(WorkflowCommandEnvelope envelope, DateTimeOffset preparedAt)
        {
            Sequence = envelope.Sequence;
            ActionDigest = envelope.ActionDigest;
            AuthorityEpoch = envelope.AuthorityEpoch;
            ApprovalDigest = envelope.ApprovalDigest;
            PreparedAt = preparedAt;
        }

        public long Sequence { get; }
        public string ActionDigest { get; }
        public long AuthorityEpoch { get; }
        public string? ApprovalDigest { get; }
        public DateTimeOffset PreparedAt { get; }
        public DateTimeOffset? DispatchedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public WorkflowCommandState State { get; set; } = WorkflowCommandState.Prepared;
        public StoredHttpResponse? Response { get; set; }
        public TaskCompletionSource<StoredHttpResponse> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class WorkflowCommandLedgerOptions
{
    public int MaxActiveLedgers { get; init; } = 8;
    public int MaxCommandsPerRun { get; init; } = 2_000;
    public int MaxRetainedTerminalLedgers { get; init; } = 32;
    public TimeSpan TerminalRetention { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxStoredResponseBytes { get; init; } = 64 * 1024;
}

internal sealed class WorkflowRunControlRequest
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }
    [JsonPropertyName("agentInstanceId")]
    public string? AgentInstanceId { get; set; }
    [JsonPropertyName("authorityEpoch")]
    public long AuthorityEpoch { get; set; }
    [JsonPropertyName("approvalDigest")]
    public string? ApprovalDigest { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

internal sealed class WorkflowRunControlStatus
{
    [JsonPropertyName("ok")]
    public bool Ok { get; private init; }
    [JsonPropertyName("alreadyTerminal")]
    public bool AlreadyTerminal { get; private init; }
    [JsonPropertyName("state")]
    public string? State { get; private init; }
    [JsonPropertyName("error")]
    public string? Error { get; private init; }
    [JsonPropertyName("reason")]
    public string? Reason { get; private init; }
    [JsonPropertyName("commands")]
    public int Commands { get; private init; }
    [JsonPropertyName("authorityEpoch")]
    public long? AuthorityEpoch { get; private init; }
    [JsonPropertyName("agentInstanceId")]
    public string? AgentInstanceId { get; private init; }
    [JsonIgnore]
    public int StatusCode { get; private init; }

    public static WorkflowRunControlStatus Success(WorkflowCommandLedger.WorkflowRunLedger run, bool alreadyTerminal)
        => new()
        {
            Ok = true,
            AlreadyTerminal = alreadyTerminal,
            State = run.IsActive ? "active" : run.TerminalReason,
            Commands = run.Commands.Count,
            AuthorityEpoch = run.AuthorityEpoch,
            AgentInstanceId = run.Key.AgentInstanceId,
            StatusCode = 200
        };

    public static WorkflowRunControlStatus Failure(int statusCode, string reason, string error)
        => new()
        {
            Error = error,
            Reason = reason,
            StatusCode = statusCode
        };
}

internal sealed class WorkflowCommandPreparation
{
    private WorkflowCommandPreparation(HttpResponse? response, WorkflowCommandReservation? reservation)
    {
        Response = response;
        Reservation = reservation;
    }

    public static WorkflowCommandPreparation None { get; } = new(null, null);
    public HttpResponse? Response { get; }
    public WorkflowCommandReservation? Reservation { get; }
    public bool ShouldExecute => Response is null && Reservation is not null;

    public static WorkflowCommandPreparation Execute(WorkflowCommandReservation reservation)
        => new(null, reservation);

    public static WorkflowCommandPreparation FromResponse(HttpResponse response)
        => new(response, null);
}

internal readonly record struct WorkflowLedgerKey(string AgentInstanceId, string RunId);

internal readonly record struct WorkflowCommandReservation(WorkflowLedgerKey RunKey, string CommandId);

internal enum WorkflowCommandState
{
    Prepared,
    Dispatched,
    Completed,
    UnknownCompletion
}

internal sealed class WorkflowCommandEnvelope
{
    public string RunId { get; init; } = "";
    public string AgentInstanceId { get; init; } = "";
    public long Sequence { get; init; }
    public string CommandId { get; init; } = "";
    public string ActionDigest { get; init; } = "";
    public long AuthorityEpoch { get; init; }
    public string? ApprovalDigest { get; init; }
}

internal static class WorkflowCommandHeaders
{
    public const string RunId = "X-DevFlow-Workflow-Run";
    public const string AgentInstanceId = "X-DevFlow-Workflow-Agent-Instance";
    public const string Sequence = "X-DevFlow-Workflow-Sequence";
    public const string CommandId = "X-DevFlow-Workflow-Command";
    public const string ActionDigest = "X-DevFlow-Workflow-Digest";
    public const string AuthorityEpoch = "X-DevFlow-Workflow-Epoch";
    public const string ApprovalDigest = "X-DevFlow-Workflow-Approval";

    public static bool HasAny(HttpRequest request)
        => request.Headers.Keys.Any(static key =>
            key.StartsWith("X-DevFlow-Workflow-", StringComparison.OrdinalIgnoreCase));

    public static bool TryRead(
        HttpRequest request,
        out WorkflowCommandEnvelope envelope,
        out HttpResponse? error)
    {
        envelope = new WorkflowCommandEnvelope();
        error = null;

        request.Headers.TryGetValue(RunId, out var runId);
        request.Headers.TryGetValue(AgentInstanceId, out var agentInstanceId);
        request.Headers.TryGetValue(Sequence, out var sequenceText);
        request.Headers.TryGetValue(CommandId, out var commandId);
        request.Headers.TryGetValue(ActionDigest, out var actionDigest);
        request.Headers.TryGetValue(AuthorityEpoch, out var authorityEpochText);
        request.Headers.TryGetValue(ApprovalDigest, out var approvalDigest);

        if (!IsSafeIdentifier(runId, 128) ||
            !IsSafeIdentifier(agentInstanceId, 128) ||
            !IsSafeIdentifier(commandId, 128) ||
            !IsDigest(actionDigest) ||
            !long.TryParse(sequenceText, out var sequence) ||
            sequence < 1 ||
            !long.TryParse(authorityEpochText, out var authorityEpoch) ||
            authorityEpoch < 1 ||
            (!string.IsNullOrWhiteSpace(approvalDigest) && !IsSafeIdentifier(approvalDigest, 128)))
        {
            error = HttpResponse.Error(
                "A complete, valid workflow command envelope is required.",
                400,
                "workflow-envelope");
            return false;
        }

        envelope = new WorkflowCommandEnvelope
        {
            RunId = runId!,
            AgentInstanceId = agentInstanceId!,
            Sequence = sequence,
            CommandId = commandId!,
            ActionDigest = actionDigest!,
            AuthorityEpoch = authorityEpoch,
            ApprovalDigest = string.IsNullOrWhiteSpace(approvalDigest) ? null : approvalDigest.Trim()
        };
        return true;
    }

    private static bool IsDigest(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsSafeIdentifier(string? value, int maximumLength)
        => value is { Length: > 0 } &&
           value.Length <= maximumLength &&
           value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
}

internal static class WorkflowCommandDigest
{
    public static string Compute(string method, string canonicalPath, string? body)
    {
        var material = string.Concat(
            method.Trim().ToUpperInvariant(),
            "\n",
            canonicalPath,
            "\n",
            CanonicalizeBody(body));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    public static string CanonicalPath(HttpRequest request)
    {
        if (request.QueryParams.Count == 0)
            return request.Path;

        var query = request.QueryParams
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Value, StringComparer.Ordinal)
            .Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        return $"{request.Path}?{string.Join("&", query)}";
    }

    private static string CanonicalizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            WriteCanonicalJson(writer, document.RootElement);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}

internal sealed class StoredHttpResponse
{
    private StoredHttpResponse(int statusCode, string statusText, string contentType, string? body, byte[]? bodyBytes)
    {
        StatusCode = statusCode;
        StatusText = statusText;
        ContentType = contentType;
        Body = body;
        BodyBytes = bodyBytes;
    }

    public int StatusCode { get; }
    public string StatusText { get; }
    public string ContentType { get; }
    public string? Body { get; }
    public byte[]? BodyBytes { get; }

    public static StoredHttpResponse From(HttpResponse response)
        => new(
            response.StatusCode,
            response.StatusText,
            response.ContentType,
            response.Body,
            response.BodyBytes?.ToArray());

    public static StoredHttpResponse UnknownCompletion()
        => From(HttpResponse.Error(
            "The workflow command completion is unknown.",
            409,
            "workflow-unknown-completion"));

    public HttpResponse ToHttpResponse() => new()
    {
        StatusCode = StatusCode,
        StatusText = StatusText,
        ContentType = ContentType,
        Body = Body,
        BodyBytes = BodyBytes?.ToArray()
    };
}
