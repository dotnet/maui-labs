using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Driver;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

internal static class FlowCompatibilityMapper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Testing.MauiFlow ToTesting(MauiFlow value) =>
        Convert<MauiFlow, Testing.MauiFlow>(value);

    public static MauiFlow FromTesting(Testing.MauiFlow value) =>
        Convert<Testing.MauiFlow, MauiFlow>(value);

    public static Testing.FlowSelector? ToTesting(FlowSelector? value) =>
        value is null ? null : Convert<FlowSelector, Testing.FlowSelector>(value);

    public static Testing.FlowStepArgs? ToTesting(FlowStepArgs? value) =>
        value is null ? null : Convert<FlowStepArgs, Testing.FlowStepArgs>(value);

    public static List<Testing.FlowAssert>? ToTesting(List<FlowAssert>? value) =>
        value is null ? null : Convert<List<FlowAssert>, List<Testing.FlowAssert>>(value);

    public static FlowReplayReport FromTesting(Testing.FlowReplayReport value) =>
        Convert<Testing.FlowReplayReport, FlowReplayReport>(value);

    public static FlowValidation FromTesting(Testing.FlowValidation value)
    {
        var result = new FlowValidation();
        result.Errors.AddRange(value.Errors);
        result.Warnings.AddRange(value.Warnings);
        return result;
    }

    private static TTarget Convert<TSource, TTarget>(TSource value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<TTarget>(json, Options)
            ?? throw new InvalidOperationException(
                $"Could not project {typeof(TSource).FullName} to {typeof(TTarget).FullName}.");
    }
}

/// <summary>
/// Compatibility facade for callers compiled against the original CLI flow recorder.
/// New code should use <see cref="Testing.FlowRecorder"/> directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use Microsoft.Maui.DevFlow.Testing.FlowRecorder instead.")]
public sealed class FlowRecorder
{
    public const int MaxSteps = Testing.FlowRecorder.MaxSteps;

    private readonly Testing.FlowRecorder _implementation;

    public FlowRecorder(string name, string? app, string? platform, string? preconditions)
        => _implementation = new Testing.FlowRecorder(
            name,
            app,
            platform,
            preconditions,
            restoredFlow: new Testing.MauiFlow
            {
                Schema = MauiFlow.CurrentSchema,
                Name = string.IsNullOrWhiteSpace(name) ? "scenario" : name.Trim(),
                App = app,
                Platform = platform,
                Preconditions = preconditions
            });

    internal FlowRecorder(Testing.FlowRecorder implementation)
        => _implementation = implementation;

    internal Testing.FlowRecorder Implementation => _implementation;

    public string Name => _implementation.Name;
    public DateTimeOffset CreatedAtUtc => _implementation.CreatedAtUtc;
    public DateTimeOffset LastTouchedUtc => _implementation.LastTouchedUtc;
    public int StepCount => _implementation.StepCount;

    public void Touch() => _implementation.Touch();

    public int AppendStep(
        string action,
        FlowSelector? target,
        string? value,
        FlowStepArgs? args,
        string? page,
        bool navigated,
        List<FlowAssert>? asserts) =>
        _implementation.AppendStep(
            action,
            FlowCompatibilityMapper.ToTesting(target),
            value,
            FlowCompatibilityMapper.ToTesting(args),
            page,
            navigated,
            FlowCompatibilityMapper.ToTesting(asserts));

    public MauiFlow Snapshot() =>
        FlowCompatibilityMapper.FromTesting(_implementation.Snapshot());

    internal (MauiFlow? Flow, FlowValidation Validation, bool Empty) ValidateAndFinish()
    {
        var result = _implementation.ValidateAndFinish();
        return (
            result.Flow is null ? null : FlowCompatibilityMapper.FromTesting(result.Flow),
            FlowCompatibilityMapper.FromTesting(result.Validation),
            result.Empty);
    }

    public MauiFlow Finish() =>
        FlowCompatibilityMapper.FromTesting(_implementation.Finish());
}

/// <summary>
/// Compatibility facade for callers compiled against the original CLI flow replayer.
/// New code should use <see cref="Testing.FlowReplayer"/> directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use Microsoft.Maui.DevFlow.Testing.FlowReplayer instead.")]
public sealed class FlowReplayer
{
    private readonly Testing.FlowReplayer _implementation;

    public FlowReplayer(AgentClient agent, int pollTries = 4, int pollGapMs = 300)
        => _implementation = new Testing.FlowReplayer(agent, pollTries, pollGapMs);

    internal Testing.FlowReplayer Implementation => _implementation;

    public async Task<FlowReplayReport> ReplayAsync(
        MauiFlow flow,
        string? file = null,
        CancellationToken ct = default)
    {
        var result = await _implementation
            .ReplayAsync(FlowCompatibilityMapper.ToTesting(flow), file, ct)
            .ConfigureAwait(false);
        return FlowCompatibilityMapper.FromTesting(result);
    }
}

public sealed class FlowSelector
{
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("typeIndex")] public FlowTypeIndex? TypeIndex { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("index")] public int? Index { get; set; }
    [JsonPropertyName("selectorKind")] public string? SelectorKind { get; set; }
    [JsonPropertyName("matchCount")] public int? MatchCount { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("fragilityReasons")] public List<string>? FragilityReasons { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrEmpty(AutomationId) &&
        string.IsNullOrEmpty(Text) &&
        string.IsNullOrEmpty(Id) &&
        TypeIndex is null &&
        !(SelectorKind == "typeIndex" && !string.IsNullOrEmpty(Type) && Index is not null);
}

public sealed class FlowTypeIndex
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class FlowAssert
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("selector")] public FlowSelector? Selector { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("expected")] public string? Expected { get; set; }
    [JsonPropertyName("verify")] public bool Verify { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class FlowStepArgs
{
    [JsonPropertyName("selector")] public FlowSelector? Selector { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("valueSource")] public string? ValueSource { get; set; }
    [JsonPropertyName("secretEnvironmentVariable")] public string? SecretEnvironmentVariable { get; set; }
    [JsonPropertyName("element")] public string? Element { get; set; }
    [JsonPropertyName("dx")] public double? Dx { get; set; }
    [JsonPropertyName("dy")] public double? Dy { get; set; }
    [JsonPropertyName("itemIndex")] public int? ItemIndex { get; set; }
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("animated")] public bool? Animated { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class FlowStep
{
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("target")] public FlowSelector? Target { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("args")] public FlowStepArgs? Args { get; set; }
    [JsonPropertyName("page")] public string? Page { get; set; }
    [JsonPropertyName("navigated")] public bool Navigated { get; set; }
    [JsonPropertyName("fragile")] public bool Fragile { get; set; }
    [JsonPropertyName("screenshot")] public string? Screenshot { get; set; }
    [JsonPropertyName("asserts")] public List<FlowAssert>? Asserts { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class MauiFlow
{
    public const int CurrentSchema = 1;

    [JsonPropertyName("schema")] public int Schema { get; set; } = CurrentSchema;
    [JsonPropertyName("name")] public string Name { get; set; } = "scenario";
    [JsonPropertyName("app")] public string? App { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("recordedAt")] public string? RecordedAt { get; set; }
    [JsonPropertyName("preconditions")] public string? Preconditions { get; set; }
    [JsonPropertyName("steps")] public List<FlowStep> Steps { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public static class FlowActions
{
    public const string Tap = "tap";
    public const string Fill = "fill";
    public const string Scroll = "scroll";
    public const string Navigate = "navigate";
    public const string Back = "back";
    public const string SetTheme = "setTheme";
    public const string SetProperty = "setProperty";
    public const string Assert = "assert";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Tap, Fill, Scroll, Navigate, Back, SetTheme, SetProperty, Assert
    };
}

public sealed class FlowParseResult
{
    public bool Ok { get; private init; }
    public MauiFlow? Flow { get; private init; }
    public string? Error { get; private init; }
    public string? File { get; private init; }

    public static FlowParseResult Success(MauiFlow flow, string? file) =>
        new() { Ok = true, Flow = flow, File = file };

    public static FlowParseResult Fail(string error, string? file = null) =>
        new() { Ok = false, Error = error, File = file };
}

public static class FlowMarkdown
{
    public static FlowParseResult Parse(string markdown, string? file = null)
    {
        var result = Testing.FlowMarkdown.Parse(markdown, file);
        return result.Ok
            ? FlowParseResult.Success(FlowCompatibilityMapper.FromTesting(result.Flow!), result.File)
            : FlowParseResult.Fail(result.Error ?? "Flow parsing failed.", result.File);
    }

    public static string Serialize(MauiFlow flow) =>
        Testing.FlowMarkdown.Serialize(FlowCompatibilityMapper.ToTesting(flow));
}

public sealed class FlowValidation
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Ok => Errors.Count == 0;
}

public static class FlowValidator
{
    public static FlowValidation Validate(MauiFlow flow) =>
        FlowCompatibilityMapper.FromTesting(
            Testing.FlowValidator.Validate(FlowCompatibilityMapper.ToTesting(flow)));
}

public sealed class FlowAssertResult
{
    public string Kind { get; set; } = "";
    public bool? Ok { get; set; }
    public bool Skipped { get; set; }
    public string? Name { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
}

public sealed class FlowStepResult
{
    public int Seq { get; set; }
    public string Action { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? FailureKind { get; set; }
    public int? MatchCount { get; set; }
    public string? SelectorQuality { get; set; }
    public List<FlowAssertResult> Asserts { get; set; } = new();
}

public sealed class FlowReplayReport
{
    public bool Ok { get; set; }
    public string Name { get; set; } = "scenario";
    public string? File { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int? DivergencePoint { get; set; }
    public bool StoppedEarly { get; set; }
    public bool EvidenceAvailable { get; set; }
    public string? EvidencePath { get; set; }
    [JsonPropertyName("report")]
    public Testing.MauiFlowRunReport? StructuredReport { get; set; }
    [JsonPropertyName("reportDigest")]
    public string? ReportDigest { get; set; }
    [JsonPropertyName("reportPath")]
    public string? ReportPath { get; set; }
    public List<FlowStepResult> Results { get; set; } = new();
}

#pragma warning disable CS0618
public sealed class FlowRecordingStore
{
    public const int MaxActive = 32;
    public static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(30);
    public static FlowRecordingStore Instance { get; } = new();

    private readonly ConcurrentDictionary<string, FlowRecorder> _sessions =
        new(StringComparer.Ordinal);

    private FlowRecordingStore()
    {
    }

    public string? Start(string name, string? app, string? platform, string? preconditions)
    {
        EvictIdle();
        if (_sessions.Count >= MaxActive)
            return null;

        var recorder = new FlowRecorder(name, app, platform, preconditions);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            if (_sessions.TryAdd(id, recorder))
                return id;
        }
        return null;
    }

    public bool TryGet(string id, out FlowRecorder recorder)
    {
        if (!string.IsNullOrEmpty(id) && _sessions.TryGetValue(id, out recorder!))
        {
            recorder.Touch();
            return true;
        }
        recorder = null!;
        return false;
    }

    public FlowRecorder? Remove(string id) =>
        !string.IsNullOrEmpty(id) && _sessions.TryRemove(id, out var recorder)
            ? recorder
            : null;

    public IReadOnlyList<(string Id, string Name, int Steps)> List()
    {
        EvictIdle();
        return _sessions
            .Select(pair => (pair.Key, pair.Value.Name, pair.Value.StepCount))
            .ToList();
    }

    internal void Reset() => _sessions.Clear();

    private void EvictIdle()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleTtl;
        foreach (var pair in _sessions)
        {
            if (pair.Value.LastTouchedUtc < cutoff)
                _sessions.TryRemove(pair.Key, out _);
        }
    }
    #pragma warning restore CS0618
}
