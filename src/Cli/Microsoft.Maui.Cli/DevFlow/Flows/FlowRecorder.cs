namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Accumulates a workflow recording in memory as a <see cref="MauiFlow"/>, one host-reported step
/// at a time. Pure (no agent dependency) and thread-safe, so interleaved MCP record calls are safe.
/// Recordings serialized via <see cref="FlowMarkdown"/> replay unchanged under
/// <see cref="FlowReplayer"/>.
/// </summary>
public sealed class FlowRecorder
{
    /// <summary>Hard cap on recorded steps, to bound memory for a runaway recording.</summary>
    public const int MaxSteps = 5000;

    private readonly object _gate = new();
    private readonly MauiFlow _flow;
    private int _seq;
    private bool _closed;

    public FlowRecorder(string name, string? app, string? platform, string? preconditions)
    {
        _flow = new MauiFlow
        {
            Name = string.IsNullOrWhiteSpace(name) ? "scenario" : name.Trim(),
            App = app,
            Platform = platform,
            Preconditions = preconditions,
        };
        CreatedAtUtc = DateTimeOffset.UtcNow;
        LastTouchedUtc = CreatedAtUtc;
    }

    public string Name => _flow.Name;

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>When this recording was last started/appended/inspected; drives idle eviction.</summary>
    public DateTimeOffset LastTouchedUtc { get; private set; }

    /// <summary>Marks the recording as recently used (called when it is looked up).</summary>
    public void Touch()
    {
        lock (_gate) LastTouchedUtc = DateTimeOffset.UtcNow;
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
    {
        lock (_gate)
        {
            if (_closed || _flow.Steps.Count >= MaxSteps)
                return -1;

            LastTouchedUtc = DateTimeOffset.UtcNow;
            var seq = ++_seq;
            _flow.Steps.Add(new FlowStep
            {
                Seq = seq,
                Action = action,
                Target = target,
                Value = value,
                Args = args,
                Page = page,
                Navigated = navigated,
                Fragile = target is not null && string.IsNullOrEmpty(target.AutomationId) && !target.IsEmpty,
                Asserts = asserts is { Count: > 0 } ? asserts : null,
            });
            return seq;
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
    internal (MauiFlow? Flow, FlowValidation Validation, bool Empty) ValidateAndFinish()
    {
        lock (_gate)
        {
            var snapshot = CopyLocked();
            var validation = FlowValidator.Validate(snapshot);
            var empty = snapshot.Steps.Count == 0;
            if (empty || !validation.Ok)
                return (null, validation, empty);

            _closed = true;
            _flow.RecordedAt ??= DateTimeOffset.UtcNow.ToString("o");
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
            _flow.RecordedAt ??= DateTimeOffset.UtcNow.ToString("o");
            return CopyLocked();
        }
    }

    private MauiFlow CopyLocked() => new()
    {
        Schema = _flow.Schema,
        Name = _flow.Name,
        App = _flow.App,
        Platform = _flow.Platform,
        RecordedAt = _flow.RecordedAt,
        Preconditions = _flow.Preconditions,
        Steps = new List<FlowStep>(_flow.Steps),
    };
}
