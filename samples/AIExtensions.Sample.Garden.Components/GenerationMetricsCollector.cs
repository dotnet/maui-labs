using System.Globalization;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Components;

public sealed record GenerationMetricsSnapshot
{
    public required string Mode { get; init; }
    public TimeSpan? MainLatency { get; init; }
    public long? MainInputTokens { get; init; }
    public long? MainOutputTokens { get; init; }
    public TimeSpan? ComposerLatency { get; init; }
    public long? ComposerInputTokens { get; init; }
    public long? ComposerOutputTokens { get; init; }
    public CompositionPlanSource? PlanSource { get; init; }
    public bool? PlanValid { get; init; }
    public int CorrectionCount { get; init; }
    public CompositionRenderDiff? RenderDiff { get; init; }
}

/// <summary>Last-turn diagnostics shown in the Garden sample for baseline/composer comparison.</summary>
public sealed class GenerationMetricsCollector
{
    private readonly object _gate = new();
    private GenerationMetricsSnapshot _snapshot = new() { Mode = "Component Composer" };

    public event EventHandler? Updated;

    public GenerationMetricsSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return _snapshot;
        }
    }

    public string Summary => Format(Snapshot);

    public void BeginTurn(string mode)
        => Update(new GenerationMetricsSnapshot { Mode = mode });

    public void RecordComposition(
        ComponentCompositionResult composition,
        CompositionRenderDiff renderDiff)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(renderDiff);

        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                ComposerLatency = composition.ModelLatency,
                ComposerInputTokens = composition.InputTokens,
                ComposerOutputTokens = composition.OutputTokens,
                PlanSource = composition.Source,
                PlanValid = composition.Validation.IsValid,
                CorrectionCount = composition.CorrectionCount,
                RenderDiff = renderDiff,
            };
        }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void CompleteMain(TimeSpan latency, long? inputTokens, long? outputTokens)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                MainLatency = latency,
                MainInputTokens = inputTokens,
                MainOutputTokens = outputTokens,
            };
        }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(string mode) => BeginTurn(mode);

    private void Update(GenerationMetricsSnapshot snapshot)
    {
        lock (_gate)
            _snapshot = snapshot;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static string Format(GenerationMetricsSnapshot snapshot)
    {
        var main = $"main {Duration(snapshot.MainLatency)} | tokens {Tokens(snapshot.MainInputTokens, snapshot.MainOutputTokens)}";
        if (snapshot.PlanSource is null)
            return $"{snapshot.Mode} | {main}";

        var diff = snapshot.RenderDiff;
        var stability = diff is null
            ? "render n/a"
            : $"{(diff.ScaffoldReused ? "scaffold reused" : "scaffold mounted")} | " +
              $"added {diff.Added.Count}, reused {diff.Reused.Count}, moved {diff.Moved.Count}, " +
              $"reconfigured {diff.Reconfigured.Count}, removed {diff.Removed.Count}";
        return $"{snapshot.Mode} | {main} | composer {Duration(snapshot.ComposerLatency)} | " +
               $"tokens {Tokens(snapshot.ComposerInputTokens, snapshot.ComposerOutputTokens)} | " +
               $"plan {snapshot.PlanSource} {(snapshot.PlanValid == true ? "valid" : "invalid")} | " +
               $"corrections {snapshot.CorrectionCount} | {stability}";
    }

    private static string Duration(TimeSpan? value)
        => value is null
            ? "n/a"
            : $"{value.Value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms";

    private static string Tokens(long? input, long? output)
        => input is null && output is null
            ? "n/a"
            : $"{input?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}/" +
              $"{output?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}";
}
