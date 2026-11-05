using System.Collections.Concurrent;
using Microsoft.Maui.Cli.DevFlow.Flows;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

internal sealed class BrokerFlowCoordinator
{
    private readonly ConcurrentDictionary<string, ActiveRecording> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _gates = new(StringComparer.Ordinal);

    public BrokerFlowResult Start(
        string agentId,
        string name,
        string? app,
        string? platform,
        string? preconditions)
    {
        lock (Gate(agentId))
            return StartCore(agentId, name, app, platform, preconditions);
    }

    private BrokerFlowResult StartCore(
        string agentId,
        string name,
        string? app,
        string? platform,
        string? preconditions)
    {
        if (_active.TryGetValue(agentId, out var existing))
        {
            if (FlowRecordingStore.Instance.TryGet(existing.RecordingId, out var existingRecorder))
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

        var recordingId = FlowRecordingStore.Instance.Start(name, app, platform, preconditions);
        if (recordingId is null)
            return BrokerFlowResult.Failure($"Too many active recordings (max {FlowRecordingStore.MaxActive}).");

        var active = new ActiveRecording(recordingId);
        if (!_active.TryAdd(agentId, active))
        {
            FlowRecordingStore.Instance.Remove(recordingId);
            return BrokerFlowResult.Failure("A recording is already active for this app.");
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

    public BrokerFlowResult Status(string agentId)
    {
        lock (Gate(agentId))
            return StatusCore(agentId);
    }

    private BrokerFlowResult StatusCore(string agentId)
    {
        if (!_active.TryGetValue(agentId, out var active))
            return new BrokerFlowResult { Ok = true, Recording = false, Steps = 0 };
        if (!FlowRecordingStore.Instance.TryGet(active.RecordingId, out var recorder))
        {
            _active.TryRemove(agentId, out _);
            return new BrokerFlowResult { Ok = true, Recording = false, Steps = 0 };
        }

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

    private BrokerFlowResult ObserveCore(string agentId, FlowObservation observation, string? expectedRecordingId)
    {
        if (!_active.TryGetValue(agentId, out var active))
            return new BrokerFlowResult { Ok = true, Recording = false, Steps = 0 };
        if (!MatchesExpected(active, expectedRecordingId))
            return BrokerFlowResult.Failure($"Unknown recordingId '{expectedRecordingId}'.");
        if (!FlowRecordingStore.Instance.TryGet(active.RecordingId, out var recorder))
            return BrokerFlowResult.Failure("The active recording no longer exists.");

        var added = FlowRecordTools.AddStepCore(
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
            observation.AssertsJson);
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
        if (!FlowRecordingStore.Instance.TryGet(active.RecordingId, out var recorder))
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

        var finished = FlowRecordTools.FinishToMarkdownCore(recorder);
        if (!finished.ok)
            return BrokerFlowResult.Failure(finished.error ?? "Could not serialize recording.");

        FlowRecordingStore.Instance.Remove(active.RecordingId);
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
            if (!FlowRecordingStore.Instance.TryGet(active.RecordingId, out var recorder))
                return BrokerFlowResult.Failure("The active recording no longer exists.");
            if (recorder.StepCount != 0)
                return BrokerFlowResult.Failure("Recording is no longer empty.");

            FlowRecordingStore.Instance.Remove(active.RecordingId);
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
        FlowRecordingStore.Instance.Remove(active.RecordingId);
        _active.TryRemove(agentId, out _);
        return new BrokerFlowResult { Ok = true, Recording = false };
    }

    public void RemoveAgent(string agentId)
    {
        lock (Gate(agentId))
        {
            if (_active.TryRemove(agentId, out var active))
                FlowRecordingStore.Instance.Remove(active.RecordingId);
        }
    }

    public void Clear()
    {
        foreach (var agentId in _active.Keys)
            RemoveAgent(agentId);
        _gates.Clear();
    }

    private object Gate(string agentId) => _gates.GetOrAdd(agentId, static _ => new object());

    private static bool MatchesExpected(ActiveRecording active, string? expectedRecordingId)
        => string.IsNullOrWhiteSpace(expectedRecordingId) ||
            string.Equals(active.RecordingId, expectedRecordingId, StringComparison.Ordinal);

    private sealed record ActiveRecording(string RecordingId);
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
