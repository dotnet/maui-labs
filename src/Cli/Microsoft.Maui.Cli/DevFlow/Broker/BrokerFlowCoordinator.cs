using System.Collections.Concurrent;
using CliFlows = Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

internal sealed class BrokerFlowCoordinator
{
    private readonly ConcurrentDictionary<string, ActiveRecording> _active = new(StringComparer.Ordinal);
    private readonly object[] _gates = Enumerable.Range(0, 64)
        .Select(static _ => new object())
        .ToArray();
    private readonly FlowRecordingStore _recordings;
    private readonly FlowRecordingSpoolStore _spools;
    private readonly object _adoptionGate = new();

    public BrokerFlowCoordinator(
        FlowRecordingStore? recordings = null,
        FlowRecordingSpoolStore? spools = null)
    {
        _recordings = recordings ?? FlowRecordingStore.Instance;
        _spools = spools ?? new FlowRecordingSpoolStore();
        RestoreSpools();
    }

    public BrokerFlowResult Start(
        string agentId,
        string name,
        string? app,
        string? platform,
        string? preconditions,
        string? sessionId = null,
        string? stableAgentId = null)
    {
        lock (Gate(agentId))
            return StartCore(
                agentId,
                name,
                app,
                platform,
                preconditions,
                sessionId,
                stableAgentId ?? agentId);
    }

    private BrokerFlowResult StartCore(
        string agentId,
        string name,
        string? app,
        string? platform,
        string? preconditions,
        string? sessionId,
        string stableAgentId)
    {
        if (_active.TryGetValue(agentId, out var existing))
        {
            if (existing.DurabilityError is not null)
                return DurabilityFailure(existing, "The active recording is not durable");
            if (_recordings.TryGet(existing.RecordingId, out var existingRecorder))
            {
                return new BrokerFlowResult
                {
                    Ok = true,
                    Recording = true,
                    RecordingId = existing.RecordingId,
                    Name = existingRecorder.Name,
                    Steps = existingRecorder.StepCount
                };
            }
            _active.TryRemove(agentId, out _);
        }

        var recordingId = _recordings.Start(name, app, platform, preconditions);
        if (recordingId is null)
            return BrokerFlowResult.Failure($"Too many active recordings (max {FlowRecordingStore.MaxActive}).");

        var active = new ActiveRecording(recordingId, stableAgentId, sessionId)
        {
            Connected = true
        };
        if (!_active.TryAdd(agentId, active))
        {
            _recordings.Remove(recordingId);
            return BrokerFlowResult.Failure("A recording is already active for this app.");
        }
        if (_recordings.TryGet(recordingId, out var started))
        {
            var persistenceError = Persist(agentId, active, started);
            if (persistenceError is not null)
            {
                _active.TryRemove(agentId, out _);
                _recordings.Remove(recordingId);
                return BrokerFlowResult.Failure($"Could not start a durable workflow recording: {persistenceError}");
            }
        }

        return new BrokerFlowResult
        {
            Ok = true,
            RecordingId = recordingId,
            Name = string.IsNullOrWhiteSpace(name) ? "scenario" : name.Trim(),
            Steps = 0,
            Recording = true
        };
    }

    public BrokerFlowResult Status(string agentId, string? expectedRecordingId = null)
    {
        lock (Gate(agentId))
            return StatusCore(agentId, expectedRecordingId);
    }

    private BrokerFlowResult StatusCore(string agentId, string? expectedRecordingId)
    {
        if (!_active.TryGetValue(agentId, out var active))
        {
            if (!string.IsNullOrWhiteSpace(expectedRecordingId))
                return BrokerFlowResult.Failure($"Unknown recordingId '{expectedRecordingId}'.");
            return new BrokerFlowResult { Ok = true, Recording = false, Steps = 0 };
        }
        if (!MatchesExpected(active, expectedRecordingId))
            return BrokerFlowResult.Failure($"Unknown recordingId '{expectedRecordingId}'.");
        if (active.DurabilityError is not null)
            return DurabilityFailure(active, "The active recording is not durable");
        if (!_recordings.TryGet(active.RecordingId, out var recorder))
        {
            _active.TryRemove(agentId, out _);
            return new BrokerFlowResult { Ok = true, Recording = false, Steps = 0 };
        }
        var persistenceError = Persist(agentId, active, recorder);
        if (persistenceError is not null)
        {
            active.DurabilityError = persistenceError;
            PersistDurabilityFailure(agentId, active, recorder, persistenceError);
            return new BrokerFlowResult
            {
                Ok = false,
                Recording = true,
                RecordingId = active.RecordingId,
                Name = recorder.Name,
                Steps = recorder.StepCount,
                Error = $"The active recording is not durably persisted: {persistenceError}"
            };
        }
        active.LastDurableSteps = recorder.StepCount;

        return new BrokerFlowResult
        {
            Ok = true,
            Recording = true,
            RecordingId = active.RecordingId,
            Name = recorder.Name,
            Steps = recorder.StepCount
        };
    }

    public BrokerFlowResult Observe(string agentId, FlowObservation observation, string? expectedRecordingId = null)
    {
        lock (Gate(agentId))
            return ObserveCore(agentId, observation, expectedRecordingId);
    }

    public BrokerFlowResult ObserveDeviceTap(
        string agentId,
        double x,
        double y,
        string? nativeId = null,
        string? nativeText = null)
    {
        lock (Gate(agentId))
        {
            if (!_active.TryGetValue(agentId, out var active))
                return new BrokerFlowResult { Ok = true, Recording = false, Steps = 0 };
            if (active.DurabilityError is not null)
                return DurabilityFailure(active, "The active recording is not durable");
            if (!_recordings.TryGet(active.RecordingId, out var recorder))
                return BrokerFlowResult.Failure("The active recording no longer exists.");

            var step = new DeviceStep
            {
                AfterStep = recorder.StepCount,
                Action = "tap",
                X = x,
                Y = y,
                NativeId = nativeId,
                NativeText = nativeText,
            };
            var itemCount = recorder.AppendExtensionArrayItem(
                DeviceStep.ExtensionKey,
                step.ToJsonElement());
            if (itemCount < 0)
                return BrokerFlowResult.Failure("The device step could not be appended to the recording.");

            var persistenceError = Persist(agentId, active, recorder);
            if (persistenceError is not null)
            {
                recorder.TryRollbackLastExtensionArrayItem(DeviceStep.ExtensionKey, itemCount);
                active.DurabilityError = persistenceError;
                PersistDurabilityFailure(agentId, active, recorder, persistenceError);
                return BrokerFlowResult.Failure(
                    $"The device tap succeeded, but its workflow step was not durably recorded: {persistenceError}");
            }

            return new BrokerFlowResult
            {
                Ok = true,
                Recording = true,
                RecordingId = active.RecordingId,
                Name = recorder.Name,
                Steps = recorder.StepCount,
                Seq = step.AfterStep,
                Fragile = step.IsFragile,
            };
        }
    }

    private BrokerFlowResult ObserveCore(string agentId, FlowObservation observation, string? expectedRecordingId)
    {
        if (!_active.TryGetValue(agentId, out var active))
            return new BrokerFlowResult { Ok = true, Recording = false, Steps = 0 };
        if (!MatchesExpected(active, expectedRecordingId))
            return BrokerFlowResult.Failure($"Unknown recordingId '{expectedRecordingId}'.");
        if (active.DurabilityError is not null)
            return DurabilityFailure(active, "The active recording is not durable");
        if (!_recordings.TryGet(active.RecordingId, out var recorder))
            return BrokerFlowResult.Failure("The active recording no longer exists.");

        var targetValueMayBeSecret = observation.Action == FlowActions.Fill ||
            (observation.Action == FlowActions.SetProperty &&
             (string.Equals(observation.Name, "Text", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(observation.Name, "Value", StringComparison.OrdinalIgnoreCase)));
        var sensitive = observation.Sensitive ||
            FlowSecretReference.LooksSensitive(observation.Name) ||
            (targetValueMayBeSecret && FlowSecretReference.LooksSensitive(
                observation.AutomationId,
                observation.Type));
        var added = CliFlows.FlowRecordTools.AddStepCore(
            recorder,
            observation.Action,
            observation.AutomationId,
            observation.Text,
            observation.Type,
            observation.Index,
            observation.Id,
            observation.Value,
            observation.Name,
            observation.Dx,
            observation.Dy,
            observation.ItemIndex,
            observation.Position,
            observation.Page,
            observation.Navigated,
            observation.AssertsJson,
            observation.MatchCount,
            observation.Quality,
            observation.FragilityReasons,
            observation.ValueSource,
            sensitive,
            observation.SelectorObservation);
        if (added.ok)
        {
            var persistenceError = Persist(agentId, active, recorder);
            if (persistenceError is not null)
            {
                recorder.TryRollbackLastStep(added.seq);
                active.DurabilityError = persistenceError;
                PersistDurabilityFailure(agentId, active, recorder, persistenceError);
                return new BrokerFlowResult
                {
                    Ok = false,
                    Recording = true,
                    RecordingId = active.RecordingId,
                    Name = recorder.Name,
                    Steps = recorder.StepCount,
                    Error = $"The mutation succeeded, but its workflow step was not durably recorded: {persistenceError}"
                };
            }
            active.LastDurableSteps = recorder.StepCount;
        }
        return added.ok
            ? new BrokerFlowResult
            {
                Ok = true,
                Recording = true,
                RecordingId = active.RecordingId,
                Name = recorder.Name,
                Steps = added.stepCount,
                Seq = added.seq,
                Fragile = added.fragile
            }
            : BrokerFlowResult.Failure(added.error ?? "Could not record mutation.");
    }

    public BrokerFlowResult Stop(string agentId, string? expectedRecordingId = null)
    {
        lock (Gate(agentId))
            return StopCore(agentId, expectedRecordingId);
    }

    private BrokerFlowResult StopCore(string agentId, string? expectedRecordingId)
    {
        if (!_active.TryGetValue(agentId, out var active))
        {
            return BrokerFlowResult.Failure("No recording is active for this app.");
        }
        if (!MatchesExpected(active, expectedRecordingId))
            return BrokerFlowResult.Failure($"Unknown recordingId '{expectedRecordingId}'.");
        if (active.DurabilityError is not null)
            return DurabilityFailure(active, "The recording cannot be completed because a mutation was not durably recorded");
        if (!_recordings.TryGet(active.RecordingId, out var recorder))
        {
            _active.TryRemove(agentId, out _);
            return BrokerFlowResult.Failure("The active recording no longer exists.");
        }

        var finalization = recorder.ValidateAndFinish();
        if (finalization.Empty)
        {
            return new BrokerFlowResult
            {
                Ok = false,
                Recording = true,
                RecordingId = active.RecordingId,
                Empty = true,
                Error = "Recording has no steps."
            };
        }
        if (finalization.Flow is null)
            return BrokerFlowResult.Failure(
                "Recording has validation errors: " + string.Join("; ", finalization.Validation.Errors));

        var finished = CliFlows.FlowRecordTools.FinishToMarkdownCore(recorder);
        if (!finished.ok)
            return BrokerFlowResult.Failure(finished.error ?? "Could not serialize recording.");

        if (!_spools.Delete(active.RecordingId))
        {
            return new BrokerFlowResult
            {
                Ok = false,
                Recording = true,
                RecordingId = active.RecordingId,
                Name = recorder.Name,
                Steps = recorder.StepCount,
                Error = "The completed recording could not be retired from durable storage; retry Stop."
            };
        }
        _recordings.Remove(active.RecordingId);
        _active.TryRemove(agentId, out _);
        return new BrokerFlowResult
        {
            Ok = true,
            Recording = false,
            RecordingId = active.RecordingId,
            Name = finished.flow!.Name,
            Steps = finished.flow.Steps.Count,
            Markdown = finished.markdown,
            Warnings = finalization.Validation.Warnings.ToArray()
        };
    }

    public BrokerFlowResult Cancel(string agentId, string? expectedRecordingId = null)
    {
        lock (Gate(agentId))
            return CancelCore(agentId, expectedRecordingId);
    }

    public BrokerFlowResult CancelIfEmpty(string agentId, string? expectedRecordingId = null)
    {
        lock (Gate(agentId))
        {
            if (!_active.TryGetValue(agentId, out var active))
                return BrokerFlowResult.Failure("No recording is active for this app.");
            if (!MatchesExpected(active, expectedRecordingId))
                return BrokerFlowResult.Failure($"Unknown recordingId '{expectedRecordingId}'.");
            if (!_recordings.TryGet(active.RecordingId, out var recorder))
                return BrokerFlowResult.Failure("The active recording no longer exists.");
            if (recorder.StepCount != 0)
                return BrokerFlowResult.Failure("Recording is no longer empty.");

            if (!_spools.Delete(active.RecordingId))
                return BrokerFlowResult.Failure("The empty recording could not be retired from durable storage.");
            _recordings.Remove(active.RecordingId);
            _active.TryRemove(agentId, out _);
            return new BrokerFlowResult { Ok = true, Recording = false, RecordingId = active.RecordingId, Empty = true };
        }
    }

    private BrokerFlowResult CancelCore(string agentId, string? expectedRecordingId)
    {
        if (!_active.TryGetValue(agentId, out var active))
        {
            return BrokerFlowResult.Failure("No recording is active for this app.");
        }
        if (!MatchesExpected(active, expectedRecordingId))
            return BrokerFlowResult.Failure($"Unknown recordingId '{expectedRecordingId}'.");
        if (!_spools.Delete(active.RecordingId))
            return BrokerFlowResult.Failure("The recording could not be retired from durable storage.");
        _recordings.Remove(active.RecordingId);
        _active.TryRemove(agentId, out _);
        return new BrokerFlowResult { Ok = true, Recording = false };
    }

    public void RemoveAgent(string agentId)
    {
        lock (_adoptionGate)
        {
            lock (Gate(agentId))
            {
                if (_active.TryGetValue(agentId, out var active))
                    active.Connected = false;
            }
        }
    }

    public bool ConnectAgent(
        string agentId,
        string stableAgentId,
        string? sessionId,
        string? expectedRecordingId = null)
    {
        lock (_adoptionGate)
        {
            if (_active.TryGetValue(agentId, out var current))
            {
                current.Connected = true;
                return true;
            }
            if (string.IsNullOrWhiteSpace(expectedRecordingId))
                return false;

            var candidates = _active
                .Where(pair =>
                    !pair.Value.Connected &&
                    string.Equals(
                        pair.Value.RecordingId,
                        expectedRecordingId,
                        StringComparison.Ordinal) &&
                    string.Equals(pair.Value.StableAgentId, stableAgentId, StringComparison.Ordinal) &&
                    SessionsCompatible(pair.Value.SessionId, sessionId))
                .ToArray();
            if (candidates.Length != 1)
                return false;

            var orphan = candidates[0];
            lock (Gate(orphan.Key))
            {
                if (!_active.TryRemove(new KeyValuePair<string, ActiveRecording>(orphan.Key, orphan.Value)))
                    return false;
                orphan.Value.Connected = true;
                if (!_active.TryAdd(agentId, orphan.Value))
                {
                    orphan.Value.Connected = false;
                    _active.TryAdd(orphan.Key, orphan.Value);
                    return false;
                }

                if (_recordings.TryGet(orphan.Value.RecordingId, out var recorder))
                {
                    var persistenceError = Persist(agentId, orphan.Value, recorder);
                    if (persistenceError is not null)
                    {
                        orphan.Value.DurabilityError = persistenceError;
                        PersistDurabilityFailure(agentId, orphan.Value, recorder, persistenceError);
                    }
                }
                return true;
            }
        }
    }

    public void Clear()
    {
        // Broker shutdown must not erase recoverable recordings. Their snapshots have already been
        // persisted after every mutation; release only in-memory indexes for process restart.
        _active.Clear();
    }

    private string? Persist(string agentId, ActiveRecording active, FlowRecorder recorder)
    {
        try
        {
            _spools.Save(
                agentId,
                active.StableAgentId,
                active.SessionId,
                active.RecordingId,
                recorder);
            return null;
        }
        catch (Exception ex)
        {
            var message = ex.GetBaseException().Message;
            _spools.ReportWarning($"Could not persist workflow recording '{active.RecordingId}': {message}");
            return message;
        }
    }

    private void RestoreSpools()
    {
        foreach (var spool in _spools.Restore())
        {
            var recorder = new FlowRecorder(
                spool.Flow.Name,
                spool.Flow.App,
                spool.Flow.Platform,
                spool.Flow.Preconditions,
                createdAtUtc: spool.CreatedUtc,
                lastTouchedUtc: spool.LastTouchedUtc,
                restoredFlow: spool.Flow);
            if (_recordings.TryRestore(spool.RecordingId, recorder))
                _active.TryAdd(
                    spool.AgentId,
                    new ActiveRecording(
                        spool.RecordingId,
                        string.IsNullOrWhiteSpace(spool.StableAgentId)
                            ? spool.AgentId
                            : spool.StableAgentId,
                        spool.SessionId)
                {
                    LastDurableSteps = spool.Flow.Steps.Count,
                    DurabilityError = spool.DurabilityError,
                    Connected = false
                });
        }
    }

    private void PersistDurabilityFailure(
        string agentId,
        ActiveRecording active,
        FlowRecorder recorder,
        string error)
    {
        try
        {
            _spools.Save(
                agentId,
                active.StableAgentId,
                active.SessionId,
                active.RecordingId,
                recorder,
                error);
        }
        catch
        {
            // Never allow an older healthy-looking snapshot to survive a durability failure.
            _ = _spools.Delete(active.RecordingId);
        }
    }

    private object Gate(string agentId)
        => _gates[(int)((uint)StringComparer.Ordinal.GetHashCode(agentId) % (uint)_gates.Length)];

    private static bool MatchesExpected(ActiveRecording active, string? expectedRecordingId)
        => string.IsNullOrWhiteSpace(expectedRecordingId) ||
            string.Equals(active.RecordingId, expectedRecordingId, StringComparison.Ordinal);

    private static bool SessionsCompatible(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ||
           string.IsNullOrWhiteSpace(second) ||
           string.Equals(first, second, StringComparison.Ordinal);

    private static BrokerFlowResult DurabilityFailure(ActiveRecording active, string prefix) => new()
    {
        Ok = false,
        Recording = true,
        RecordingId = active.RecordingId,
        Steps = active.LastDurableSteps,
        Error = $"{prefix}: {active.DurabilityError}. Cancel this recording and start again."
    };

    private sealed class ActiveRecording
    {
        public ActiveRecording(string recordingId, string stableAgentId, string? sessionId)
        {
            RecordingId = recordingId;
            StableAgentId = stableAgentId;
            SessionId = sessionId;
        }

        public string RecordingId { get; }
        public string StableAgentId { get; }
        public string? SessionId { get; }
        public string? DurabilityError { get; set; }
        public int LastDurableSteps { get; set; }
        public bool Connected { get; set; }
    }
}

internal sealed class FlowObservation
{
    public string Action { get; set; } = "";
    public string? AutomationId { get; set; }
    public string? Text { get; set; }
    public string? Type { get; set; }
    public int? Index { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public string? Name { get; set; }
    public double? Dx { get; set; }
    public double? Dy { get; set; }
    public int? ItemIndex { get; set; }
    public string? Position { get; set; }
    public string? Page { get; set; }
    public bool Navigated { get; set; }
    public string? AssertsJson { get; set; }
    public int? MatchCount { get; set; }
    public string? Quality { get; set; }
    public string[]? FragilityReasons { get; set; }
    public string? ValueSource { get; set; }
    public bool Sensitive { get; set; }
    public Microsoft.Maui.DevFlow.Driver.MauiSelectorObservation? SelectorObservation { get; set; }
}

internal sealed class BrokerFlowResult
{
    public bool Ok { get; set; }
    public bool Recording { get; set; }
    public string? RecordingId { get; set; }
    public string? Name { get; set; }
    public int Steps { get; set; }
    public int? Seq { get; set; }
    public bool Fragile { get; set; }
    public bool Empty { get; set; }
    public string? Markdown { get; set; }
    public string[]? Warnings { get; set; }
    public string? Error { get; set; }

    public static BrokerFlowResult Failure(string error) => new()
    {
        Ok = false,
        Error = error
    };
}
