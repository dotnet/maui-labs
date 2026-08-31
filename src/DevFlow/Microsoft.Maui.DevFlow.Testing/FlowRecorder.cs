using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Accumulates a workflow recording in memory as a <see cref="MauiFlow"/>, one host-reported step
/// at a time. Pure (no agent dependency) and thread-safe, so interleaved host record calls are safe.
/// Recordings serialized via <see cref="FlowMarkdown"/> replay unchanged under
/// <see cref="FlowReplayer"/>.
/// </summary>
public sealed class FlowRecorder
{
    /// <summary>Hard cap on recorded steps, to bound memory for a runaway recording.</summary>
    public const int MaxSteps = 5000;

    private readonly object _gate = new();
    private readonly MauiFlow _flow;
    private readonly TimeProvider _clock;
    private int _seq;
    private bool _closed;

    public FlowRecorder(
        string name,
        string? app,
        string? platform,
        string? preconditions)
        : this(
            name,
            app,
            platform,
            preconditions,
            clock: null,
            createdAtUtc: null,
            lastTouchedUtc: null,
            restoredFlow: null)
    {
    }

    /// <summary>
    /// Creates a recorder from an existing flow snapshot. This is useful for hosts that persist
    /// and restore an in-progress recording.
    /// </summary>
    public FlowRecorder(
        string name,
        string? app,
        string? platform,
        string? preconditions,
        TimeProvider? clock = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? lastTouchedUtc = null,
        MauiFlow? restoredFlow = null)
    {
        _clock = clock ?? TimeProvider.System;
        _flow = restoredFlow is null ? new MauiFlow
        {
            Name = string.IsNullOrWhiteSpace(name) ? "scenario" : name.Trim(),
            App = app,
            Platform = platform,
            Preconditions = preconditions,
        } : Clone(restoredFlow);
        _seq = _flow.Steps.Count == 0 ? 0 : _flow.Steps.Max(static step => step.Seq);
        CreatedAtUtc = createdAtUtc ?? _clock.GetUtcNow();
        LastTouchedUtc = lastTouchedUtc ?? CreatedAtUtc;
    }

    public string Name => _flow.Name;

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>When this recording was last started/appended/inspected; drives idle eviction.</summary>
    public DateTimeOffset LastTouchedUtc { get; private set; }

    /// <summary>Marks the recording as recently used (called when it is looked up).</summary>
    public void Touch()
    {
        lock (_gate) LastTouchedUtc = _clock.GetUtcNow();
    }

    public int StepCount
    {
        get { lock (_gate) return _flow.Steps.Count; }
    }

    /// <summary>
    /// Appends a host-reported step and returns its assigned <c>seq</c>, or <c>-1</c> if the step
    /// cap is reached. A step whose <paramref name="target"/> carries no AutomationId is flagged
    /// <c>fragile</c> (only an AutomationId is a durable selector).
    /// </summary>
    public int AppendStep(string action, FlowSelector? target, string? value, FlowStepArgs? args, string? page, bool navigated, List<FlowAssert>? asserts)
        => AppendStep(action, target, value, args, page, navigated, asserts, selectorEvidence: null);

    /// <summary>
    /// Appends a host-reported step together with value-free selector evidence. The evidence is
    /// diagnostic only and cannot alter the active replay selector.
    /// </summary>
    public int AppendStep(
        string action,
        FlowSelector? target,
        string? value,
        FlowStepArgs? args,
        string? page,
        bool navigated,
        List<FlowAssert>? asserts,
        MauiSelectorEvidence? selectorEvidence)
    {
        lock (_gate)
        {
            if (_closed || _flow.Steps.Count >= MaxSteps)
                return -1;

            LastTouchedUtc = _clock.GetUtcNow();
            var seq = ++_seq;
            _flow.Steps.Add(new FlowStep
            {
                Seq = seq,
                StepId = MauiFlowStepIdentity.Create(seq),
                Action = action,
                Target = target,
                Value = value,
                Args = args,
                Page = page,
                Navigated = navigated,
                Fragile = FlowSelector.IsFragile(target),
                Asserts = asserts is { Count: > 0 } ? asserts : null,
                SelectorEvidence = selectorEvidence,
            });
            return seq;
        }
    }

    /// <summary>Removes the most recently appended step when it has the supplied sequence.</summary>
    public bool TryRollbackLastStep(int sequence)
    {
        lock (_gate)
        {
            if (_flow.Steps.Count == 0 || _flow.Steps[^1].Seq != sequence)
                return false;
            _flow.Steps.RemoveAt(_flow.Steps.Count - 1);
            _seq = _flow.Steps.Count == 0 ? 0 : _flow.Steps[^1].Seq;
            return true;
        }
    }

    /// <summary>
    /// Appends one bounded item to a top-level extension array and returns the new item count.
    /// Host-specific recorders use this without introducing a dependency from Testing to the host.
    /// </summary>
    public int AppendExtensionArrayItem(string key, JsonElement item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            if (_closed)
                return -1;

            var existing = _flow.ExtensionData is not null &&
                _flow.ExtensionData.TryGetValue(key, out var value)
                ? value
                : default;
            if (existing.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Array))
                return -1;

            var count = existing.ValueKind == JsonValueKind.Array
                ? existing.GetArrayLength()
                : 0;
            if (count >= MaxSteps)
                return -1;

            _flow.ExtensionData ??= [];
            _flow.ExtensionData[key] = AppendArrayItem(existing, item);
            LastTouchedUtc = _clock.GetUtcNow();
            return count + 1;
        }
    }

    /// <summary>Rolls back the most recently appended extension item after persistence fails.</summary>
    public bool TryRollbackLastExtensionArrayItem(string key, int expectedCount)
    {
        lock (_gate)
        {
            if (_flow.ExtensionData is null ||
                !_flow.ExtensionData.TryGetValue(key, out var existing) ||
                existing.ValueKind != JsonValueKind.Array ||
                existing.GetArrayLength() != expectedCount ||
                expectedCount <= 0)
            {
                return false;
            }

            _flow.ExtensionData[key] = TakeArrayItems(existing, expectedCount - 1);
            return true;
        }
    }

    /// <summary>Returns a snapshot copy of the flow so far, without stamping or closing it (for
    /// validation and status). Safe against a concurrent <see cref="AppendStep"/>.</summary>
    public MauiFlow Snapshot()
    {
        lock (_gate) return CopyLocked();
    }

    /// <summary>
    /// Validates and closes the recording under the same lock so a concurrent observation cannot
    /// append an unvalidated step between validation and final serialization.
    /// </summary>
    public (MauiFlow? Flow, FlowValidation Validation, bool Empty) ValidateAndFinish()
    {
        lock (_gate)
        {
            var snapshot = CopyLocked();
            var validation = FlowValidator.Validate(snapshot);
            var empty = snapshot.Steps.Count == 0;
            if (empty || !validation.Ok)
                return (null, validation, empty);

            _closed = true;
            _flow.RecordedAt ??= _clock.GetUtcNow().ToString("o");
            return (CopyLocked(), validation, false);
        }
    }

    /// <summary>
    /// Closes the recording (further appends fail), stamps <c>recordedAt</c>, and returns a snapshot
    /// copy ready to serialize. Idempotent: a second call still returns the final snapshot.
    /// </summary>
    public MauiFlow Finish()
    {
        lock (_gate)
        {
            _closed = true;
            _flow.RecordedAt ??= _clock.GetUtcNow().ToString("o");
            return CopyLocked();
        }
    }

    private MauiFlow CopyLocked() => MauiFlowClone.Clone(_flow);

    private static MauiFlow Clone(MauiFlow source) => MauiFlowClone.Clone(source);

    private static JsonElement AppendArrayItem(JsonElement existing, JsonElement item)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            if (existing.ValueKind == JsonValueKind.Array)
            {
                foreach (var current in existing.EnumerateArray())
                    current.WriteTo(writer);
            }
            item.WriteTo(writer);
            writer.WriteEndArray();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static JsonElement TakeArrayItems(JsonElement existing, int count)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            var written = 0;
            foreach (var current in existing.EnumerateArray())
            {
                if (written++ >= count)
                    break;
                current.WriteTo(writer);
            }
            writer.WriteEndArray();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }
}
