using Microsoft.Maui.DevFlow.Driver;
using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Shared, capability-honest actionability checks for workflow drivers. It checks only what every
/// agent reports (resolution, visibility, enabled state and bounds); it deliberately makes no
/// cross-platform claim about occlusion.
/// </summary>
public sealed class FlowActionabilityEngine
{
    private readonly IMauiFlowDriver _driver;
    private readonly int _tries;
    private readonly int _gapMs;
    private readonly Action<FlowActionabilityObservation>? _observe;

    public FlowActionabilityEngine(AgentClient agent, int tries = 4, int gapMs = 300)
        : this(new AgentClientMauiFlowDriver(agent), tries, gapMs)
    {
    }

    public FlowActionabilityEngine(
        IMauiFlowDriver driver,
        int tries = 4,
        int gapMs = 300,
        Action<FlowActionabilityObservation>? observe = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _tries = Math.Max(1, tries);
        _gapMs = Math.Max(0, gapMs);
        _observe = observe;
    }

    public async Task<FlowTargetResolution> WaitForActionableAsync(
        FlowSelector? selector,
        bool requireStableBounds,
        CancellationToken cancellationToken = default)
    {
        FlowTargetResolution? last = null;
        for (var attempt = 0; attempt < _tries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            var resolution = await ResolveAsync(selector, cancellationToken);
            last = resolution;
            Observe(attempt + 1, "resolution", resolution, null, elapsed: ElapsedMilliseconds(started), outcome: resolution.Ok ? "resolved" : resolution.Kind);
            if (!resolution.Ok)
            {
                if (resolution.Kind == FlowFailureKinds.Ambiguous)
                    return resolution;
            }
            else if (!resolution.Element!.IsVisible)
            {
                last = resolution.WithFailure(FlowFailureKinds.NotVisible, "The resolved element is not visible.");
                Observe(attempt + 1, "visibility", last, null, elapsed: 0, outcome: last.Kind);
            }
            else if (!resolution.Element.IsEnabled)
            {
                last = resolution.WithFailure(FlowFailureKinds.Disabled, "The resolved element is disabled.");
                Observe(attempt + 1, "enabled", last, null, elapsed: 0, outcome: last.Kind);
            }
            else if (requireStableBounds)
            {
                var bounds = resolution.Element.Bounds;
                if (bounds is null || bounds.Width <= 0 || bounds.Height <= 0)
                {
                    last = resolution.WithFailure(FlowFailureKinds.Unstable, "The tap target has empty or non-positive bounds.");
                    Observe(attempt + 1, "bounds", last, false, elapsed: 0, outcome: last.Kind);
                }
                else
                {
                    var stableStarted = Stopwatch.GetTimestamp();
                    await Task.Delay(Math.Max(40, Math.Min(_gapMs, 150)), cancellationToken);
                    var second = await _driver.GetElementAsync(resolution.Element.Id);
                    if (second is not null && SameBounds(bounds, second.Bounds))
                    {
                        Observe(attempt + 1, "bounds", resolution, true, ElapsedMilliseconds(stableStarted), "stable");
                        return resolution;
                    }
                    last = resolution.WithFailure(
                        FlowFailureKinds.Unstable,
                        "The tap target bounds changed while waiting for a stable layout.");
                    Observe(attempt + 1, "bounds", last, false, ElapsedMilliseconds(stableStarted), last.Kind);
                }
            }
            else
            {
                return resolution;
            }

            if (attempt < _tries - 1)
                await Task.Delay(_gapMs, cancellationToken);
        }
        return last ?? FlowTargetResolution.Failure(FlowFailureKinds.NotFound, "No target selector was supplied.");
    }

    public async Task<FlowTargetResolution> WaitForResolvedAsync(
        FlowSelector? selector,
        CancellationToken cancellationToken = default)
    {
        FlowTargetResolution? last = null;
        for (var attempt = 0; attempt < _tries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            var resolution = await ResolveAsync(selector, cancellationToken);
            last = resolution;
            Observe(attempt + 1, "resolution", resolution, null, ElapsedMilliseconds(started), resolution.Ok ? "resolved" : resolution.Kind);
            if (resolution.Ok || resolution.Kind == FlowFailureKinds.Ambiguous)
                return resolution;
            if (attempt < _tries - 1)
                await Task.Delay(_gapMs, cancellationToken);
        }
        return last ?? FlowTargetResolution.Failure(
            FlowFailureKinds.NotFound,
            "No target selector was supplied.");
    }

    public async Task<FlowTargetResolution> ResolveAsync(FlowSelector? selector, CancellationToken cancellationToken = default)
    {
        if (selector is null || selector.IsEmpty)
            return FlowTargetResolution.Failure(FlowFailureKinds.NotFound, "No target selector was supplied.");
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ElementInfo> matches;
        if (!string.IsNullOrEmpty(selector.AutomationId))
        {
            matches = await _driver.QueryAsync(automationId: selector.AutomationId);
            if (!string.IsNullOrWhiteSpace(selector.StableItemKey) ||
                !string.IsNullOrWhiteSpace(selector.CollectionScope))
            {
                if (!selector.HasScopedStableItem)
                {
                    return FlowTargetResolution.Failure(
                        FlowFailureKinds.NotFound,
                        "A scoped item selector requires AutomationId, stableItemKey, and collectionScope.");
                }
                if (!FlowSelector.IsOpaqueStableItemKey(selector.StableItemKey))
                {
                    return FlowTargetResolution.Failure(
                        FlowFailureKinds.NotFound,
                        "A stable item key must be an opaque SHA-256 identity.");
                }
                matches = matches
                    .Where(element =>
                        string.Equals(element.StableItemKey, selector.StableItemKey, StringComparison.Ordinal) &&
                        string.Equals(element.CollectionScope, selector.CollectionScope, StringComparison.Ordinal))
                    .ToArray();
                return Exact(matches, "scoped item", "stable-item-key");
            }
            return Exact(matches, "AutomationId");
        }
        if (!string.IsNullOrEmpty(selector.Text))
        {
            matches = (await _driver.QueryAsync(text: selector.Text))
                .Where(element => string.Equals(element.Text, selector.Text, StringComparison.Ordinal))
                .ToArray();
            return Exact(matches, "text");
        }
        var typeIndex = selector.TypeIndex ??
            (selector.SelectorKind == "typeIndex" && !string.IsNullOrEmpty(selector.Type) && selector.Index is not null
                ? new FlowTypeIndex { Type = selector.Type, Index = selector.Index.Value }
                : null);
        if (typeIndex is not null && !string.IsNullOrEmpty(typeIndex.Type))
        {
            matches = await _driver.QueryAsync(type: typeIndex.Type);
            if (typeIndex.Index < 0 || typeIndex.Index >= matches.Count)
                return FlowTargetResolution.Failure(FlowFailureKinds.NotFound, $"Type+index selector found {matches.Count} candidate(s), not index {typeIndex.Index}.", matches);
            return FlowTargetResolution.Success(matches[typeIndex.Index], matches.Count, "fragile");
        }
        if (!string.IsNullOrEmpty(selector.Id))
        {
            var element = await _driver.GetElementAsync(selector.Id);
            return element is null
                ? FlowTargetResolution.Failure(FlowFailureKinds.NotFound, "Raw element id was not found.")
                : FlowTargetResolution.Success(element, 1, "fragile");
        }
        return FlowTargetResolution.Failure(FlowFailureKinds.NotFound, "No usable target selector was supplied.");
    }

    private static FlowTargetResolution Exact(
        IReadOnlyList<ElementInfo> matches,
        string selectorKind,
        string? quality = null)
    {
        if (matches.Count == 1)
            return FlowTargetResolution.Success(matches[0], 1, quality ?? selectorKind);
        if (matches.Count == 0)
            return FlowTargetResolution.Failure(FlowFailureKinds.NotFound, $"{selectorKind} selector found no elements.", matches);
        return FlowTargetResolution.Failure(
            FlowFailureKinds.Ambiguous,
            $"{selectorKind} selector is ambiguous: {matches.Count} elements matched ({CandidateSummary(matches)}).",
            matches);
    }

    private static string CandidateSummary(IReadOnlyList<ElementInfo> matches)
        => string.Join(", ", matches.Take(3).Select(static element =>
            string.IsNullOrWhiteSpace(element.AutomationId)
                ? $"{element.Type}#{element.Id}"
                : $"{element.Type}[{element.AutomationId}]"));

    private static bool SameBounds(BoundsInfo first, BoundsInfo? second)
        => second is not null &&
           first.X == second.X && first.Y == second.Y &&
           first.Width == second.Width && first.Height == second.Height;

    private void Observe(
        int attempt,
        string kind,
        FlowTargetResolution resolution,
        bool? boundsStable,
        long elapsed,
        string outcome)
    {
        if (_observe is null)
            return;

        var element = resolution.Element;
        var bounds = element?.Bounds;
        _observe(new FlowActionabilityObservation
        {
            Attempt = attempt,
            At = DateTimeOffset.UtcNow,
            Kind = kind,
            Resolved = resolution.Ok,
            Visible = element?.IsVisible,
            Enabled = element?.IsEnabled,
            HasBounds = bounds is not null && bounds.Width > 0 && bounds.Height > 0,
            BoundsStable = boundsStable,
            MatchCount = resolution.MatchCount,
            WaitDurationMs = elapsed,
            Outcome = outcome,
            Message = resolution.Error,
            Resolution = resolution,
        });
    }

    private static long ElapsedMilliseconds(long started)
        => (long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}

/// <summary>One observed poll/check emitted by <see cref="FlowActionabilityEngine"/>.</summary>
public sealed class FlowActionabilityObservation
{
    public int Attempt { get; init; }
    public DateTimeOffset At { get; init; }
    public string Kind { get; init; } = "";
    public bool Resolved { get; init; }
    public bool? Visible { get; init; }
    public bool? Enabled { get; init; }
    public bool? HasBounds { get; init; }
    public bool? BoundsStable { get; init; }
    public int MatchCount { get; init; }
    public long WaitDurationMs { get; init; }
    public string Outcome { get; init; } = "";
    public string? Message { get; init; }
    public FlowTargetResolution? Resolution { get; init; }
}

public static class FlowFailureKinds
{
    public const string NotFound = "not-found";
    public const string Ambiguous = "ambiguous";
    public const string NotVisible = "not-visible";
    public const string Disabled = "disabled";
    public const string Unstable = "unstable";
    public const string UnsafeValue = "unsafe-value";
    public const string SecretRequired = "secret-required";
    public const string Drive = "drive";
    public const string Assertion = "assertion";
    public const string Validation = "validation";
    public const string UnknownCompletion = "unknown-completion";
    public const string WorkflowCommandConflict = "workflow-command-conflict";
}

/// <summary>
/// The outcome of resolving an assertion's own selector. Failure values reuse
/// <see cref="FlowFailureKinds"/> so a reader does not have to learn a second vocabulary.
/// </summary>
public static class FlowAssertTargetStatuses
{
    /// <summary>The assertion's selector matched exactly one element, so its value was read.</summary>
    public const string Resolved = "resolved";
}

public sealed class FlowTargetResolution
{
    public bool Ok { get; init; }
    public string Kind { get; init; } = FlowFailureKinds.NotFound;
    public string? Error { get; init; }
    public ElementInfo? Element { get; init; }
    public int MatchCount { get; init; }
    public string? Quality { get; init; }
    public IReadOnlyList<ElementInfo> Candidates { get; init; } = [];

    public static FlowTargetResolution Success(ElementInfo element, int count, string quality)
        => new() { Ok = true, Element = element, MatchCount = count, Quality = quality, Candidates = [element] };

    public static FlowTargetResolution Failure(string kind, string error, IReadOnlyList<ElementInfo>? candidates = null)
        => new() { Kind = kind, Error = error, MatchCount = candidates?.Count ?? 0, Candidates = candidates ?? [] };

    public FlowTargetResolution WithFailure(string kind, string error)
        => new()
        {
            Kind = kind,
            Error = error,
            Element = Element,
            MatchCount = MatchCount,
            Quality = Quality,
            Candidates = Candidates
        };
}
