using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class DevFlowAgentService
{
    private const double MaxPointerCoordinate = 100_000;
    private const int MaxPointerSessionIdLength = 96;
    private const int MaxPointerCancelReasonLength = 64;
    private const int PointerSessionTombstoneLimit = 32;
    private static readonly TimeSpan PointerSessionTombstoneLifetime = TimeSpan.FromSeconds(30);

    private readonly object _pointerSessionStateGate = new();
    private readonly SemaphoreSlim _pointerSessionOperationGate = new(1, 1);
    private readonly Dictionary<string, PointerSessionTombstone> _pointerSessionTombstones =
        new(StringComparer.Ordinal);
    private PointerSessionState? _activePointerSession;
    private PendingPointerStart? _pendingPointerStart;
    private CancellationTokenSource? _pointerSessionWatchdogCts;
    private Task? _pointerSessionWatchdogTask;

    protected readonly record struct PointerStreamSupport(
        bool Supported,
        string Fidelity,
        bool RequiresOptIn,
        bool SupportsCoalescedSamples,
        bool SupportsPressure,
        bool SupportsButtons,
        string[] PointerTypes,
        string? Reason)
    {
        public static PointerStreamSupport Unsupported(string reason)
            => new(
                Supported: false,
                Fidelity: "unsupported",
                RequiresOptIn: false,
                SupportsCoalescedSamples: false,
                SupportsPressure: false,
                SupportsButtons: false,
                PointerTypes: [],
                Reason: reason);
    }

    protected readonly record struct PointerSessionBackendStartResult(
        bool Success,
        object? State,
        string? Detail,
        string? Error,
        int StatusCode,
        string? Reason)
    {
        public static PointerSessionBackendStartResult Started(object state, string detail)
            => new(true, state, detail, null, 200, null);

        public static PointerSessionBackendStartResult Failed(
            string error,
            int statusCode = 400,
            string? reason = "pointer-start-rejected")
            => new(false, null, null, error, statusCode, reason);
    }

    protected readonly record struct PointerSessionBackendResult(
        bool Success,
        string? Detail,
        string? Error,
        int StatusCode,
        string? Reason)
    {
        public static PointerSessionBackendResult Applied(string? detail = null)
            => new(true, detail, null, 200, null);

        public static PointerSessionBackendResult Failed(
            string error,
            int statusCode = 409,
            string? reason = "pointer-session-failed")
            => new(false, null, error, statusCode, reason);
    }

    protected sealed class PointerSessionStartRequest : CaptureBoundRequest
    {
        public string? SessionId { get; set; }
        public long PointerId { get; set; }
        public string? PointerType { get; set; }
        public string? CoordinateSpace { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double ElapsedMs { get; set; }
        public double? Pressure { get; set; }
        public int? Buttons { get; set; }
        public int? WindowId { get; set; }
        public PointerActionCorrelation? Correlation { get; set; }
    }

    protected sealed class PointerSessionMoveRequest
    {
        public int Sequence { get; set; }
        public List<PointerSessionSample> Samples { get; set; } = [];
    }

    protected sealed class PointerSessionEndRequest
    {
        public int Sequence { get; set; }
        public PointerSessionSample? Sample { get; set; }
    }

    protected sealed class PointerSessionCancelRequest
    {
        public string? Reason { get; set; }
    }

    protected sealed class PointerSessionSample
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double ElapsedMs { get; set; }
        public double? Pressure { get; set; }
        public int? Buttons { get; set; }
    }

    protected sealed class PointerActionCorrelation
    {
        public string? ActionId { get; set; }
        public string? Source { get; set; }
        public string? StreamId { get; set; }
        public string? FrameId { get; set; }
        public long? FrameRevision { get; set; }
        public string? CoordinateSpace { get; set; }
    }

    private sealed class PointerSessionReply
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("state")]
        public string State { get; set; } = "";

        [JsonPropertyName("lastSequence")]
        public int LastSequence { get; set; }

        [JsonPropertyName("idleTimeoutMs")]
        public int IdleTimeoutMs { get; set; }

        [JsonPropertyName("maxDurationMs")]
        public int MaxDurationMs { get; set; }

        [JsonPropertyName("maxSamplesPerMove")]
        public int MaxSamplesPerMove { get; set; }

        [JsonPropertyName("maxRequestBytes")]
        public int MaxRequestBytes { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class PointerSessionState
    {
        public required string SessionId { get; init; }
        public required string StartFingerprint { get; init; }
        public required string PointerType { get; init; }
        public required object BackendState { get; init; }
        public required PointerStreamSupport Support { get; init; }
        public required CancellationTokenSource Cancellation { get; init; }
        public string? LeaseId { get; init; }
        public int LastSequence { get; set; }
        public double LastElapsedMs { get; set; }
        public long StartedTicks { get; init; }
        public long LastActivityTicks { get; set; }
        public string? LastOperationFingerprint { get; set; }
        public string? LastOperationKind { get; set; }
        public PointerSessionReply? LastOperationReply { get; set; }
    }

    private sealed class PendingPointerStart
    {
        public required string SessionId { get; init; }
        public required CancellationTokenSource Cancellation { get; init; }
        public string? LeaseId { get; init; }
    }

    private sealed record PointerSessionTombstone(
        string SessionId,
        int LastSequence,
        string? OperationKind,
        string? Fingerprint,
        PointerSessionReply Reply,
        long CreatedTicks);

    protected virtual PointerStreamSupport GetPointerStreamSupport()
        => PointerStreamSupport.Unsupported(UnsupportedCapabilityReason);

    protected virtual Task<HttpResponse?> PreparePointerSessionStartAsync(
        HttpRequest request,
        PointerSessionStartRequest body)
        => Task.FromResult<HttpResponse?>(null);

    protected virtual Task<PointerSessionBackendStartResult> StartPointerSessionBackendAsync(
        PointerSessionStartRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(PointerSessionBackendStartResult.Failed(
            GetPointerStreamSupport().Reason ?? UnsupportedCapabilityReason,
            501,
            "not_supported"));

    protected virtual Task<PointerSessionBackendResult> MovePointerSessionBackendAsync(
        object state,
        IReadOnlyList<PointerSessionSample> samples,
        CancellationToken cancellationToken)
        => Task.FromResult(PointerSessionBackendResult.Failed(
            GetPointerStreamSupport().Reason ?? UnsupportedCapabilityReason,
            501,
            "not_supported"));

    protected virtual Task<PointerSessionBackendResult> EndPointerSessionBackendAsync(
        object state,
        PointerSessionSample sample,
        CancellationToken cancellationToken)
        => Task.FromResult(PointerSessionBackendResult.Failed(
            GetPointerStreamSupport().Reason ?? UnsupportedCapabilityReason,
            501,
            "not_supported"));

    protected virtual Task<PointerSessionBackendResult> CancelPointerSessionBackendAsync(
        object state,
        string reason)
        => Task.FromResult(PointerSessionBackendResult.Applied());

    protected virtual void DisposePointerSessionBackendState(object state)
    {
        if (state is IDisposable disposable)
            disposable.Dispose();
    }

    private object BuildPointerStreamCapability()
    {
        var support = GetPointerStreamSupport();
        return new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["supported"] = support.Supported,
            ["features"] = support.Supported
                ? new[]
                {
                    "start",
                    "move",
                    "end",
                    "cancel",
                    "sequence-validation",
                    "idle-timeout",
                    support.SupportsCoalescedSamples ? "coalesced" : null,
                    support.SupportsPressure ? "pressure" : null,
                    support.SupportsButtons ? "buttons" : null
                }.Where(static feature => feature is not null).ToArray()
                : [],
            ["fidelity"] = support.Fidelity,
            ["requiresOptIn"] = support.RequiresOptIn,
            ["pointerTypes"] = support.PointerTypes,
            ["pressure"] = support.SupportsPressure,
            ["buttons"] = support.SupportsButtons,
            ["maxConcurrentPointers"] = 1,
            ["maxInFlightMoves"] = 1,
            ["maxSamplesPerMove"] = PointerMaxSamplesPerMove,
            ["maxRequestBytes"] = PointerMaxRequestBytes,
            ["idleTimeoutMs"] = PointerIdleTimeoutMs,
            ["maxDurationMs"] = PointerMaxDurationMs,
            ["coordinateSpaces"] = support.Supported ? new[] { "windowLogical" } : [],
            ["reason"] = support.Reason
        };
    }

    private bool IsPointerStreamSupported => GetPointerStreamSupport().Supported;

    private int PointerMaxSamplesPerMove
        => Math.Clamp(_options.PointerSessionMaxSamplesPerMove, 1, 256);

    private int PointerMaxRequestBytes
        => Math.Clamp(_options.PointerSessionMaxRequestBytes, 1_024, 64 * 1_024);

    private int PointerIdleTimeoutMs
        => Math.Clamp(_options.PointerSessionIdleTimeoutMs, 500, 60_000);

    private int PointerMaxDurationMs
        => Math.Clamp(_options.PointerSessionMaxDurationMs, PointerIdleTimeoutMs, 5 * 60_000);

    private async Task<HttpResponse> HandlePointerSessionStart(HttpRequest request)
    {
        var support = GetPointerStreamSupport();
        if (!support.Supported)
            return NotSupported("ui.pointerStream", support.Reason);
        if (RequestTooLarge(request))
            return PointerError("", "Request body exceeds the pointer stream limit.", 413, "payload-too-large");

        PointerSessionStartRequest? body;
        try
        {
            body = request.BodyAs<PointerSessionStartRequest>();
        }
        catch (System.Text.Json.JsonException)
        {
            return PointerError("", "Invalid pointer session start payload.", 400, "invalid-request");
        }

        if (body is null)
            return PointerError("", "A pointer session start body is required.", 400, "invalid-request");
        if (ValidateSessionId(body.SessionId) is { } sessionIdError)
            return PointerError(body.SessionId ?? "", sessionIdError, 400, "invalid-session-id");
        if (ValidateSample(body.X, body.Y, body.ElapsedMs, body.Pressure, body.Buttons, support) is { } sampleError)
            return PointerError(body.SessionId!, sampleError, 422, "invalid-sample");
        if (body.ElapsedMs > PointerMaxDurationMs)
            return PointerError(body.SessionId!, "elapsedMs exceeds the maximum pointer session duration.", 422, "invalid-sample");

        var pointerType = string.IsNullOrWhiteSpace(body.PointerType)
            ? "touch"
            : body.PointerType.Trim().ToLowerInvariant();
        if (!support.PointerTypes.Contains(pointerType, StringComparer.Ordinal))
        {
            return PointerError(
                body.SessionId!,
                $"Pointer type '{pointerType}' is not supported. Supported types: {string.Join(", ", support.PointerTypes)}.",
                422,
                "unsupported-pointer-type");
        }
        if (!string.Equals(body.CoordinateSpace, "windowLogical", StringComparison.Ordinal))
        {
            return PointerError(
                body.SessionId!,
                "Pointer coordinates must use the 'windowLogical' coordinate space.",
                422,
                "unsupported-coordinate-space");
        }
        if (body.Correlation is { CoordinateSpace: { } correlationSpace }
            && !string.Equals(correlationSpace, body.CoordinateSpace, StringComparison.Ordinal))
        {
            return PointerError(
                body.SessionId!,
                "correlation.coordinateSpace must match the pointer payload coordinateSpace.",
                422,
                "invalid-correlation");
        }

        body.PointerType = pointerType;
        var fingerprint = Fingerprint(request.Body);
        await _pointerSessionOperationGate.WaitAsync(request.CancellationToken).ConfigureAwait(false);
        try
        {
            PrunePointerTombstones();
            PointerSessionState? active;
            lock (_pointerSessionStateGate)
                active = _activePointerSession;

            if (active is not null)
            {
                if (string.Equals(active.SessionId, body.SessionId, StringComparison.Ordinal)
                    && string.Equals(active.StartFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return PointerResponse(new PointerSessionReply
                    {
                        Success = true,
                        SessionId = active.SessionId,
                        State = "active",
                        LastSequence = active.LastSequence,
                        Detail = "Pointer session is already active."
                    });
                }

                return PointerError(
                    body.SessionId!,
                    $"Pointer session '{active.SessionId}' is already active.",
                    409,
                    "pointer-session-active",
                    active.LastSequence);
            }

            lock (_pointerSessionStateGate)
            {
                if (_pointerSessionTombstones.ContainsKey(body.SessionId!))
                {
                    return PointerError(
                        body.SessionId!,
                        "This pointer session id has already reached a terminal state.",
                        410,
                        "session-gone");
                }
            }

            if (await PreparePointerSessionStartAsync(request, body) is { } preparationError)
                return preparationError;

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
            cancellation.CancelAfter(PointerIdleTimeoutMs);
            var pending = new PendingPointerStart
            {
                SessionId = body.SessionId!,
                Cancellation = cancellation,
                LeaseId = GetRequestLeaseId(request)
            };
            lock (_pointerSessionStateGate)
                _pendingPointerStart = pending;
            PointerSessionBackendStartResult backend;
            try
            {
                backend = await StartPointerSessionBackendAsync(body, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                ClearPendingPointerStart(pending);
                cancellation.Dispose();
                return PointerError(
                    body.SessionId!,
                    "Pointer session start was cancelled before it completed.",
                    409,
                    "pointer-start-cancelled");
            }
            catch (Exception ex)
            {
                ClearPendingPointerStart(pending);
                cancellation.Dispose();
                return PointerError(
                    body.SessionId!,
                    $"Pointer session start failed: {ex.GetBaseException().Message}",
                    500,
                    "pointer-start-failed");
            }
            if (!backend.Success || backend.State is null)
            {
                ClearPendingPointerStart(pending);
                cancellation.Dispose();
                return PointerError(
                    body.SessionId!,
                    backend.Error ?? "The backend rejected the pointer session.",
                    backend.StatusCode,
                    backend.Reason);
            }
            cancellation.CancelAfter(Timeout.InfiniteTimeSpan);

            var now = Environment.TickCount64;
            var state = new PointerSessionState
            {
                SessionId = body.SessionId!,
                StartFingerprint = fingerprint,
                PointerType = pointerType,
                BackendState = backend.State,
                Support = support,
                Cancellation = cancellation,
                LeaseId = GetRequestLeaseId(request),
                LastSequence = 0,
                LastElapsedMs = body.ElapsedMs,
                StartedTicks = now,
                LastActivityTicks = now
            };
            var cancelledBeforeActivation = false;
            lock (_pointerSessionStateGate)
            {
                if (ReferenceEquals(_pendingPointerStart, pending))
                    _pendingPointerStart = null;
                if (cancellation.IsCancellationRequested)
                    cancelledBeforeActivation = true;
                else
                    _activePointerSession = state;
            }
            if (cancelledBeforeActivation)
            {
                try { await CancelPointerSessionBackendAsync(backend.State, "pointer-start-cancelled").ConfigureAwait(false); }
                catch { }
                try { DisposePointerSessionBackendState(backend.State); }
                catch { }
                cancellation.Dispose();
                return PointerError(
                    body.SessionId!,
                    "Pointer session start was cancelled before activation.",
                    409,
                    "pointer-start-cancelled");
            }
            EnsurePointerSessionWatchdog();

            return PointerResponse(new PointerSessionReply
            {
                Success = true,
                SessionId = state.SessionId,
                State = "active",
                LastSequence = 0,
                Detail = backend.Detail
            });
        }
        finally
        {
            _pointerSessionOperationGate.Release();
        }
    }

    private async Task<HttpResponse> HandlePointerSessionMove(HttpRequest request)
    {
        var sessionId = request.RouteParams.GetValueOrDefault("id") ?? "";
        if (RequestTooLarge(request))
            return PointerError(sessionId, "Request body exceeds the pointer stream limit.", 413, "payload-too-large");

        PointerSessionMoveRequest? body;
        try
        {
            body = request.BodyAs<PointerSessionMoveRequest>();
        }
        catch (System.Text.Json.JsonException)
        {
            return PointerError(sessionId, "Invalid pointer move payload.", 400, "invalid-request");
        }
        if (body is null)
            return PointerError(sessionId, "A pointer move body is required.", 400, "invalid-request");
        if (body.Samples.Count > PointerMaxSamplesPerMove)
        {
            return PointerError(
                sessionId,
                $"A pointer move may contain at most {PointerMaxSamplesPerMove} samples.",
                413,
                "too-many-samples");
        }

        var fingerprint = Fingerprint(request.Body);
        await _pointerSessionOperationGate.WaitAsync(request.CancellationToken).ConfigureAwait(false);
        try
        {
            var active = GetActivePointerSession(sessionId);
            if (active is null)
                return PointerTerminalOrGone(sessionId, body.Sequence, "move", fingerprint);
            if (!RequestOwnsPointerSession(request, active.LeaseId))
            {
                await CancelPointerSessionUnderGateAsync(active, "lease-lost").ConfigureAwait(false);
                return PointerError(sessionId, "The pointer session belongs to another mutation lease.", 409, "lease");
            }
            if (ValidateSequence(active, body.Sequence, "move", fingerprint) is { } sequenceResponse)
                return sequenceResponse;

            var previousElapsedMs = active.LastElapsedMs;
            foreach (var sample in body.Samples)
            {
                if (ValidateSample(
                        sample.X,
                        sample.Y,
                        sample.ElapsedMs,
                        sample.Pressure,
                        sample.Buttons,
                        active.Support) is { } sampleError)
                {
                    return PointerError(sessionId, sampleError, 422, "invalid-sample", active.LastSequence);
                }
                if (sample.ElapsedMs < previousElapsedMs)
                {
                    return PointerError(
                        sessionId,
                        "Pointer sample elapsedMs values must be monotonic.",
                        409,
                        "timestamp-regression",
                        active.LastSequence);
                }
                previousElapsedMs = sample.ElapsedMs;
                if (sample.ElapsedMs > PointerMaxDurationMs)
                {
                    return PointerError(
                        sessionId,
                        "Pointer sample elapsedMs exceeds the maximum session duration.",
                        422,
                        "invalid-sample",
                        active.LastSequence);
                }
            }

            PointerSessionBackendResult backend = PointerSessionBackendResult.Applied("Heartbeat accepted.");
            if (body.Samples.Count > 0)
            {
                try
                {
                    backend = await MovePointerSessionBackendAsync(
                        active.BackendState,
                        body.Samples,
                        active.Cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
                {
                    return await TerminatePointerSessionAfterFailureAsync(
                        active,
                        "Pointer session was cancelled while applying a move.",
                        "pointer-session-cancelled").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return await TerminatePointerSessionAfterFailureAsync(
                        active,
                        $"Pointer move failed: {ex.GetBaseException().Message}",
                        "pointer-move-failed").ConfigureAwait(false);
                }
            }

            if (!backend.Success)
            {
                return await TerminatePointerSessionAfterFailureAsync(
                    active,
                    backend.Error ?? "The backend rejected the pointer move.",
                    backend.Reason ?? "pointer-move-failed",
                    backend.StatusCode).ConfigureAwait(false);
            }

            active.LastSequence = body.Sequence;
            if (body.Samples.Count > 0)
                active.LastElapsedMs = body.Samples[^1].ElapsedMs;
            active.LastActivityTicks = Environment.TickCount64;
            var reply = new PointerSessionReply
            {
                Success = true,
                SessionId = active.SessionId,
                State = "active",
                LastSequence = active.LastSequence,
                Detail = backend.Detail
            };
            active.LastOperationFingerprint = fingerprint;
            active.LastOperationKind = "move";
            active.LastOperationReply = reply;
            return PointerResponse(reply);
        }
        finally
        {
            _pointerSessionOperationGate.Release();
        }
    }

    private async Task<HttpResponse> HandlePointerSessionEnd(HttpRequest request)
    {
        var sessionId = request.RouteParams.GetValueOrDefault("id") ?? "";
        if (RequestTooLarge(request))
            return PointerError(sessionId, "Request body exceeds the pointer stream limit.", 413, "payload-too-large");

        PointerSessionEndRequest? body;
        try
        {
            body = request.BodyAs<PointerSessionEndRequest>();
        }
        catch (System.Text.Json.JsonException)
        {
            return PointerError(sessionId, "Invalid pointer end payload.", 400, "invalid-request");
        }
        if (body?.Sample is null)
            return PointerError(sessionId, "A final pointer sample is required.", 400, "invalid-request");

        var fingerprint = Fingerprint(request.Body);
        await _pointerSessionOperationGate.WaitAsync(request.CancellationToken).ConfigureAwait(false);
        try
        {
            var active = GetActivePointerSession(sessionId);
            if (active is null)
                return PointerTerminalOrGone(sessionId, body.Sequence, "end", fingerprint);
            if (!RequestOwnsPointerSession(request, active.LeaseId))
            {
                await CancelPointerSessionUnderGateAsync(active, "lease-lost").ConfigureAwait(false);
                return PointerError(sessionId, "The pointer session belongs to another mutation lease.", 409, "lease");
            }
            if (ValidateSequence(active, body.Sequence, "end", fingerprint) is { } sequenceResponse)
                return sequenceResponse;
            if (ValidateSample(
                    body.Sample.X,
                    body.Sample.Y,
                    body.Sample.ElapsedMs,
                    body.Sample.Pressure,
                    body.Sample.Buttons,
                    active.Support) is { } sampleError)
            {
                return PointerError(sessionId, sampleError, 422, "invalid-sample", active.LastSequence);
            }
            if (body.Sample.ElapsedMs < active.LastElapsedMs)
            {
                return PointerError(
                    sessionId,
                    "The final pointer sample elapsedMs must not precede the last move.",
                    409,
                    "timestamp-regression",
                    active.LastSequence);
            }
            if (body.Sample.ElapsedMs > PointerMaxDurationMs)
            {
                return PointerError(
                    sessionId,
                    "The final pointer sample exceeds the maximum session duration.",
                    422,
                    "invalid-sample",
                    active.LastSequence);
            }

            PointerSessionBackendResult backend;
            try
            {
                backend = await EndPointerSessionBackendAsync(
                    active.BackendState,
                    body.Sample,
                    active.Cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await TerminatePointerSessionAfterFailureAsync(
                    active,
                    $"Pointer end failed: {ex.GetBaseException().Message}",
                    "pointer-end-failed").ConfigureAwait(false);
            }

            if (!backend.Success)
            {
                return await TerminatePointerSessionAfterFailureAsync(
                    active,
                    backend.Error ?? "The backend rejected the pointer end.",
                    backend.Reason ?? "pointer-end-failed",
                    backend.StatusCode).ConfigureAwait(false);
            }

            var reply = new PointerSessionReply
            {
                Success = true,
                SessionId = active.SessionId,
                State = "ended",
                LastSequence = body.Sequence,
                Detail = backend.Detail
            };
            CompletePointerSession(active, reply, "end", fingerprint);
            return PointerResponse(reply);
        }
        finally
        {
            _pointerSessionOperationGate.Release();
        }
    }

    private async Task<HttpResponse> HandlePointerSessionCancel(HttpRequest request)
    {
        var sessionId = request.RouteParams.GetValueOrDefault("id") ?? "";
        if (RequestTooLarge(request))
            return PointerError(sessionId, "Request body exceeds the pointer stream limit.", 413, "payload-too-large");

        PointerSessionCancelRequest? body;
        try
        {
            body = request.BodyAs<PointerSessionCancelRequest>();
        }
        catch (System.Text.Json.JsonException)
        {
            return PointerError(sessionId, "Invalid pointer cancel payload.", 400, "invalid-request");
        }
        var reason = CleanCancelReason(body?.Reason);
        if (!RequestMayCancelPointerSession(request, sessionId))
        {
            return PointerError(
                sessionId,
                "The pointer session belongs to another mutation lease.",
                409,
                "lease");
        }
        SignalPointerSessionCancellation(sessionId);

        await _pointerSessionOperationGate.WaitAsync(request.CancellationToken).ConfigureAwait(false);
        try
        {
            var active = GetActivePointerSession(sessionId);
            if (active is null)
            {
                lock (_pointerSessionStateGate)
                {
                    PrunePointerTombstonesNoLock();
                    if (_pointerSessionTombstones.TryGetValue(sessionId, out var tombstone))
                        return PointerResponse(tombstone.Reply);
                }

                return PointerResponse(new PointerSessionReply
                {
                    Success = true,
                    SessionId = sessionId,
                    State = "unknown",
                    LastSequence = 0,
                    Reason = "unknown-session",
                    Detail = "No active pointer session matched the supplied id."
                });
            }

            return await CancelPointerSessionUnderGateAsync(active, reason).ConfigureAwait(false);
        }
        finally
        {
            _pointerSessionOperationGate.Release();
        }
    }

    protected HttpResponse? RejectMutationDuringPointerSession(HttpRequest request)
    {
        if (IsPointerSessionPath(request.Path) || IsPointerTerminatingMutationPath(request.Path))
            return null;

        lock (_pointerSessionStateGate)
        {
            if (_activePointerSession is not { } active)
                return null;

            return PointerError(
                active.SessionId,
                "A pointer session is active. End or cancel it before starting another UI mutation.",
                409,
                "pointer-session-active",
                active.LastSequence);
        }
    }

    private static bool IsPointerSessionPath(string path)
        => path.StartsWith("/api/v1/ui/actions/pointer-sessions", StringComparison.OrdinalIgnoreCase);

    private static bool IsPointerTerminatingMutationPath(string path)
        => path is "/api/v1/ui/actions/navigate"
            or "/api/v1/ui/actions/back"
            or "/api/v1/webview/navigate";

    private async Task<HttpResponse> ExecutePointerTerminatingMutationAsync(
        HttpRequest request,
        Func<HttpRequest, Task<HttpResponse>> handler)
    {
        await CancelActivePointerSessionAsync("navigation").ConfigureAwait(false);
        return await ExecuteUiMutationAsync(request, handler).ConfigureAwait(false);
    }

    private PointerSessionState? GetActivePointerSession(string sessionId)
    {
        lock (_pointerSessionStateGate)
        {
            return _activePointerSession is { } active
                && string.Equals(active.SessionId, sessionId, StringComparison.Ordinal)
                    ? active
                    : null;
        }
    }

    private void ClearPendingPointerStart(PendingPointerStart pending)
    {
        lock (_pointerSessionStateGate)
        {
            if (ReferenceEquals(_pendingPointerStart, pending))
                _pendingPointerStart = null;
        }
    }

    private static string? GetRequestLeaseId(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.MutationLease?.LeaseId))
            return request.MutationLease.LeaseId;
        if (request.Headers.TryGetValue("X-DevFlow-Lease", out var leaseId)
            && !string.IsNullOrWhiteSpace(leaseId))
            return leaseId;
        return request.Headers.TryGetValue("X-DevFlow-Writer", out leaseId)
            && !string.IsNullOrWhiteSpace(leaseId)
                ? leaseId
                : null;
    }

    private bool RequestOwnsPointerSession(HttpRequest request, string? sessionLeaseId)
        => !_options.RequireMutationLease
            || !string.IsNullOrWhiteSpace(sessionLeaseId)
                && string.Equals(GetRequestLeaseId(request), sessionLeaseId, StringComparison.Ordinal);

    private bool RequestMayCancelPointerSession(HttpRequest request, string sessionId)
    {
        if (!_options.RequireMutationLease)
            return true;
        var requestLeaseId = GetRequestLeaseId(request);
        lock (_pointerSessionStateGate)
        {
            if (_activePointerSession is { } active
                && string.Equals(active.SessionId, sessionId, StringComparison.Ordinal))
            {
                return !string.IsNullOrWhiteSpace(active.LeaseId)
                    && string.Equals(requestLeaseId, active.LeaseId, StringComparison.Ordinal);
            }
            if (_pendingPointerStart is { } pending
                && string.Equals(pending.SessionId, sessionId, StringComparison.Ordinal))
            {
                return !string.IsNullOrWhiteSpace(pending.LeaseId)
                    && string.Equals(requestLeaseId, pending.LeaseId, StringComparison.Ordinal);
            }
        }
        return true;
    }

    private HttpResponse? ValidateSequence(
        PointerSessionState active,
        int sequence,
        string operationKind,
        string fingerprint)
    {
        if (sequence == active.LastSequence
            && string.Equals(active.LastOperationKind, operationKind, StringComparison.Ordinal)
            && string.Equals(active.LastOperationFingerprint, fingerprint, StringComparison.Ordinal)
            && active.LastOperationReply is not null)
        {
            return PointerResponse(active.LastOperationReply);
        }
        if (sequence <= active.LastSequence)
        {
            return PointerError(
                active.SessionId,
                $"Pointer sequence {sequence} conflicts with the applied sequence {active.LastSequence}.",
                409,
                "sequence-conflict",
                active.LastSequence);
        }
        if (sequence != active.LastSequence + 1)
        {
            return PointerError(
                active.SessionId,
                $"Expected pointer sequence {active.LastSequence + 1}, received {sequence}.",
                409,
                "sequence-gap",
                active.LastSequence);
        }
        return null;
    }

    private HttpResponse PointerTerminalOrGone(
        string sessionId,
        int sequence,
        string operationKind,
        string fingerprint)
    {
        lock (_pointerSessionStateGate)
        {
            PrunePointerTombstonesNoLock();
            if (_pointerSessionTombstones.TryGetValue(sessionId, out var tombstone))
            {
                if (sequence == tombstone.LastSequence
                    && string.Equals(operationKind, tombstone.OperationKind, StringComparison.Ordinal)
                    && string.Equals(fingerprint, tombstone.Fingerprint, StringComparison.Ordinal))
                {
                    return PointerResponse(tombstone.Reply);
                }

                return PointerError(
                    sessionId,
                    $"Pointer session '{sessionId}' is already {tombstone.Reply.State}.",
                    410,
                    "session-gone",
                    tombstone.LastSequence);
            }
        }

        return PointerError(sessionId, $"Pointer session '{sessionId}' was not found.", 410, "session-gone");
    }

    private async Task<HttpResponse> TerminatePointerSessionAfterFailureAsync(
        PointerSessionState active,
        string error,
        string reason,
        int statusCode = 409)
    {
        active.Cancellation.Cancel();
        PointerSessionBackendResult cancellation;
        try
        {
            cancellation = await CancelPointerSessionBackendAsync(active.BackendState, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            cancellation = PointerSessionBackendResult.Failed(
                $"Backend cancellation also failed: {ex.GetBaseException().Message}",
                500,
                "pointer-cancel-failed");
        }

        var combined = cancellation.Success || string.IsNullOrWhiteSpace(cancellation.Error)
            ? error
            : $"{error} {cancellation.Error}";
        var reply = new PointerSessionReply
        {
            Success = false,
            SessionId = active.SessionId,
            State = "cancelled",
            LastSequence = active.LastSequence,
            Reason = reason,
            Error = combined,
            Detail = cancellation.Detail
        };
        CompletePointerSession(active, reply, "cancel", fingerprint: null);
        return PointerResponse(reply, statusCode);
    }

    private async Task<HttpResponse> CancelPointerSessionUnderGateAsync(
        PointerSessionState active,
        string reason)
    {
        PointerSessionBackendResult backend;
        try
        {
            backend = await CancelPointerSessionBackendAsync(active.BackendState, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            backend = PointerSessionBackendResult.Failed(
                $"Pointer cancellation failed: {ex.GetBaseException().Message}",
                500,
                "pointer-cancel-failed");
        }

        var reply = new PointerSessionReply
        {
            Success = backend.Success,
            SessionId = active.SessionId,
            State = "cancelled",
            LastSequence = active.LastSequence,
            Reason = reason,
            Error = backend.Error,
            Detail = backend.Detail
        };
        CompletePointerSession(active, reply, "cancel", fingerprint: null);
        return PointerResponse(reply, backend.Success ? 200 : backend.StatusCode);
    }

    private void CompletePointerSession(
        PointerSessionState active,
        PointerSessionReply reply,
        string operationKind,
        string? fingerprint)
    {
        lock (_pointerSessionStateGate)
        {
            if (ReferenceEquals(_activePointerSession, active))
                _activePointerSession = null;
            PrunePointerTombstonesNoLock();
            _pointerSessionTombstones[active.SessionId] = new PointerSessionTombstone(
                active.SessionId,
                reply.LastSequence,
                operationKind,
                fingerprint,
                reply,
                Environment.TickCount64);
            while (_pointerSessionTombstones.Count > PointerSessionTombstoneLimit)
            {
                var oldest = _pointerSessionTombstones.MinBy(static pair => pair.Value.CreatedTicks);
                if (oldest.Key is null)
                    break;
                _pointerSessionTombstones.Remove(oldest.Key);
            }
        }

        active.Cancellation.Cancel();
        active.Cancellation.Dispose();
        try
        {
            DisposePointerSessionBackendState(active.BackendState);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Pointer session cleanup failed: {ex.GetBaseException().Message}");
        }
    }

    private void SignalPointerSessionCancellation(string? sessionId = null)
    {
        lock (_pointerSessionStateGate)
        {
            if (_activePointerSession is { } active
                && (sessionId is null || string.Equals(active.SessionId, sessionId, StringComparison.Ordinal)))
            {
                active.Cancellation.Cancel();
            }
            if (_pendingPointerStart is { } pending
                && (sessionId is null || string.Equals(pending.SessionId, sessionId, StringComparison.Ordinal)))
            {
                pending.Cancellation.Cancel();
            }
        }
    }

    private bool ActivePointerSessionUsesLease(string? leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId))
            return false;
        lock (_pointerSessionStateGate)
        {
            return (_activePointerSession is { LeaseId: { } activeLease }
                    && string.Equals(activeLease, leaseId, StringComparison.Ordinal))
                || (_pendingPointerStart is { LeaseId: { } pendingLease }
                    && string.Equals(pendingLease, leaseId, StringComparison.Ordinal));
        }
    }

    private bool ActivePointerSessionUsesDifferentLease(string? leaseId)
    {
        lock (_pointerSessionStateGate)
        {
            return (_activePointerSession is { LeaseId: { } activeLease }
                    && !string.Equals(activeLease, leaseId, StringComparison.Ordinal))
                || (_pendingPointerStart is { LeaseId: { } pendingLease }
                    && !string.Equals(pendingLease, leaseId, StringComparison.Ordinal));
        }
    }

    private async Task CancelActivePointerSessionAsync(
        string reason,
        PointerSessionState? expectedSession = null)
    {
        if (expectedSession is null)
        {
            SignalPointerSessionCancellation();
        }
        else
        {
            lock (_pointerSessionStateGate)
            {
                if (!ReferenceEquals(_activePointerSession, expectedSession))
                    return;
                expectedSession.Cancellation.Cancel();
            }
        }
        await _pointerSessionOperationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            PointerSessionState? active;
            lock (_pointerSessionStateGate)
                active = _activePointerSession;
            if (active is not null
                && (expectedSession is null || ReferenceEquals(active, expectedSession)))
                await CancelPointerSessionUnderGateAsync(active, reason).ConfigureAwait(false);
        }
        finally
        {
            _pointerSessionOperationGate.Release();
        }
    }

    internal Task CancelPointerSessionForHostAsync(string reason)
        => CancelActivePointerSessionAsync(CleanCancelReason(reason));

    private void EnsurePointerSessionWatchdog()
    {
        lock (_pointerSessionStateGate)
        {
            if (_pointerSessionWatchdogTask is { IsCompleted: false })
                return;
            _pointerSessionWatchdogCts?.Dispose();
            _pointerSessionWatchdogCts = new CancellationTokenSource();
            _pointerSessionWatchdogTask = RunPointerSessionWatchdogAsync(_pointerSessionWatchdogCts.Token);
        }
    }

    private async Task RunPointerSessionWatchdogAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                PointerSessionState? active;
                lock (_pointerSessionStateGate)
                    active = _activePointerSession;
                if (active is null)
                    continue;

                var now = Environment.TickCount64;
                if (now - active.StartedTicks >= PointerMaxDurationMs)
                {
                    await CancelActivePointerSessionAsync("max-duration", active).ConfigureAwait(false);
                    continue;
                }
                if (now - active.LastActivityTicks >= PointerIdleTimeoutMs)
                {
                    await CancelActivePointerSessionAsync("idle-timeout", active).ConfigureAwait(false);
                    continue;
                }
                if (_options.RequireMutationLease && !string.IsNullOrWhiteSpace(active.LeaseId))
                {
                    var lease = await _mutationLease.CheckAsync(active.LeaseId).ConfigureAwait(false);
                    if (!lease.Allowed)
                        await CancelActivePointerSessionAsync("lease-lost", active).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopPointerSessionsAsync()
    {
        CancellationTokenSource? watchdogCts;
        Task? watchdogTask;
        lock (_pointerSessionStateGate)
        {
            watchdogCts = _pointerSessionWatchdogCts;
            watchdogTask = _pointerSessionWatchdogTask;
            _pointerSessionWatchdogCts = null;
            _pointerSessionWatchdogTask = null;
        }

        watchdogCts?.Cancel();
        await CancelActivePointerSessionAsync("agent-stopped").ConfigureAwait(false);
        if (watchdogTask is not null)
        {
            try { await watchdogTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        watchdogCts?.Dispose();
    }

    private void DisposePointerSessions()
    {
        PointerSessionState? active;
        PendingPointerStart? pending;
        lock (_pointerSessionStateGate)
        {
            _pointerSessionWatchdogCts?.Cancel();
            _pointerSessionWatchdogCts?.Dispose();
            _pointerSessionWatchdogCts = null;
            _pointerSessionWatchdogTask = null;
            active = _activePointerSession;
            _activePointerSession = null;
            pending = _pendingPointerStart;
            _pendingPointerStart = null;
            _pointerSessionTombstones.Clear();
        }

        pending?.Cancellation.Cancel();
        pending?.Cancellation.Dispose();
        if (active is not null)
        {
            active.Cancellation.Cancel();
            active.Cancellation.Dispose();
            try
            {
                var cancelTask = CancelPointerSessionBackendAsync(
                    active.BackendState,
                    "agent-disposed");
                if (cancelTask.Wait(TimeSpan.FromSeconds(1)))
                {
                    DisposePointerSessionBackendState(active.BackendState);
                }
                else
                {
                    _ = cancelTask.ContinueWith(
                        task =>
                        {
                            _ = task.Exception;
                            try { DisposePointerSessionBackendState(active.BackendState); }
                            catch { }
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            catch
            {
                try { DisposePointerSessionBackendState(active.BackendState); }
                catch { }
            }
        }
        _pointerSessionOperationGate.Dispose();
    }

    private bool RequestTooLarge(HttpRequest request)
        => Encoding.UTF8.GetByteCount(request.Body ?? string.Empty) > PointerMaxRequestBytes;

    private static string Fingerprint(string? body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty)));

    private static string? ValidateSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "sessionId is required.";
        if (sessionId.Length > MaxPointerSessionIdLength)
            return $"sessionId must be at most {MaxPointerSessionIdLength} characters.";
        if (sessionId.Any(static c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            return "sessionId may contain only ASCII letters, digits, '.', '_' and '-'.";
        return null;
    }

    private static string? ValidateSample(
        double x,
        double y,
        double elapsedMs,
        double? pressure,
        int? buttons,
        PointerStreamSupport support)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)
            || Math.Abs(x) > MaxPointerCoordinate || Math.Abs(y) > MaxPointerCoordinate)
        {
            return $"Pointer coordinates must be finite and within +/-{MaxPointerCoordinate:0}.";
        }
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0)
            return "elapsedMs must be a finite non-negative value.";
        if (pressure.HasValue)
        {
            if (!support.SupportsPressure)
                return "This pointer backend does not support pressure.";
            if (!double.IsFinite(pressure.Value) || pressure.Value is < 0 or > 1)
                return "pressure must be between 0 and 1.";
        }
        if (buttons.HasValue)
        {
            if (!support.SupportsButtons)
                return "This pointer backend does not support buttons.";
            if (buttons.Value < 0)
                return "buttons must be non-negative.";
        }
        return null;
    }

    private static string CleanCancelReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "client-cancel";
        var cleaned = reason.Trim();
        if (cleaned.Length > MaxPointerCancelReasonLength)
            cleaned = cleaned[..MaxPointerCancelReasonLength];
        return new string(cleaned.Select(static c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
    }

    private HttpResponse PointerError(
        string sessionId,
        string error,
        int statusCode,
        string? reason,
        int lastSequence = 0)
        => PointerResponse(new PointerSessionReply
        {
            Success = false,
            SessionId = sessionId,
            State = statusCode == 410 ? "gone" : "rejected",
            LastSequence = lastSequence,
            Reason = reason,
            Error = error
        }, statusCode);

    private HttpResponse PointerResponse(PointerSessionReply reply, int statusCode = 200)
    {
        reply.IdleTimeoutMs = PointerIdleTimeoutMs;
        reply.MaxDurationMs = PointerMaxDurationMs;
        reply.MaxSamplesPerMove = PointerMaxSamplesPerMove;
        reply.MaxRequestBytes = PointerMaxRequestBytes;
        return HttpResponse.Json(reply, statusCode);
    }

    private void PrunePointerTombstones()
    {
        lock (_pointerSessionStateGate)
            PrunePointerTombstonesNoLock();
    }

    private void PrunePointerTombstonesNoLock()
    {
        var now = Environment.TickCount64;
        foreach (var id in _pointerSessionTombstones
            .Where(pair => now - pair.Value.CreatedTicks > PointerSessionTombstoneLifetime.TotalMilliseconds)
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _pointerSessionTombstones.Remove(id);
        }
    }
}
