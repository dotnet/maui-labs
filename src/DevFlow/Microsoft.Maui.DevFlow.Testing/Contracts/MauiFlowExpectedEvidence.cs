using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// The artifact kinds a flow may declare that a run is expected to produce. These name evidence
/// categories, not files: the run report records whether the category was produced, and never
/// compares its contents against a stored baseline.
/// </summary>
public static class MauiFlowEvidenceKinds
{
    /// <summary>A pixel capture of the application.</summary>
    public const string Screenshot = "screenshot";

    /// <summary>A structural capture of the application's visual tree.</summary>
    public const string VisualTree = "visual-tree";

    /// <summary>Application logs collected for the run.</summary>
    public const string Logs = "logs";

    /// <summary>A redacted failure-evidence bundle.</summary>
    public const string FailureEvidence = "failure-evidence";

    /// <summary>An explicitly approved recording of the surrounding device screen.</summary>
    public const string DeviceRecording = "device-recording";

    /// <summary>The run report itself.</summary>
    public const string RunReport = "run-report";

    /// <summary>A named independent business oracle result, identified by <c>reference</c>.</summary>
    public const string BusinessOracle = "business-oracle";

    /// <summary>The closed set of declarable evidence kinds.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Screenshot,
        VisualTree,
        Logs,
        FailureEvidence,
        DeviceRecording,
        RunReport,
        BusinessOracle,
    ];

    /// <summary>Whether the supplied value names a declarable evidence kind.</summary>
    public static bool IsKnown(string? value)
        => value is not null && All.Contains(value.Trim().ToLowerInvariant(), StringComparer.Ordinal);
}

/// <summary>The verdict recorded for one declared evidence expectation.</summary>
public static class MauiFlowEvidenceExpectationStates
{
    /// <summary>The run produced the declared evidence.</summary>
    public const string Satisfied = "satisfied";

    /// <summary>The run did not produce the declared evidence.</summary>
    public const string Unsatisfied = "unsatisfied";

    /// <summary>
    /// The expectation could not apply to this run, for example failure-only evidence on a run
    /// that passed. Not applicable is never counted as a miss.
    /// </summary>
    public const string NotApplicable = "not-applicable";
}

/// <summary>
/// Whether the evidence a flow declared it would produce was actually produced.
/// <para>
/// This verifies <em>collection</em>, not correctness. A satisfied expectation means the artifact
/// category exists for this run. It does not mean the artifact matches a baseline, contains the
/// right screen, or shows the right values — there is no golden-image or pixel comparison behind
/// this block.
/// </para>
/// </summary>
public sealed class MauiFlowExpectedEvidenceReport
{
    [JsonPropertyName("declared")] public int? Declared { get; set; }
    [JsonPropertyName("satisfied")] public int? Satisfied { get; set; }
    [JsonPropertyName("unsatisfied")] public int? Unsatisfied { get; set; }
    [JsonPropertyName("notApplicable")] public int? NotApplicable { get; set; }

    /// <summary>
    /// True when no declared expectation was left unsatisfied. Report-only: an unsatisfied
    /// expectation describes an evidence-collection gap in the run configuration and does not by
    /// itself change the run outcome.
    /// </summary>
    [JsonPropertyName("allSatisfied")] public bool? AllSatisfied { get; set; }

    [JsonPropertyName("checks")] public List<MauiFlowExpectedEvidenceCheck> Checks { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One declared evidence expectation and what the run did about it.</summary>
public sealed class MauiFlowExpectedEvidenceCheck
{
    [JsonPropertyName("expectationId")] public string? ExpectationId { get; set; }

    /// <summary>One of <see cref="MauiFlowEvidenceKinds"/>.</summary>
    [JsonPropertyName("kind")] public string? Kind { get; set; }

    /// <summary>Either <c>flow</c> or <c>step</c>.</summary>
    [JsonPropertyName("scope")] public string? Scope { get; set; }

    /// <summary>The declaring step, when the expectation is step-scoped.</summary>
    [JsonPropertyName("stepId")] public string? StepId { get; set; }

    /// <summary>The named target for kinds that need one, such as a business oracle id.</summary>
    [JsonPropertyName("reference")] public string? Reference { get; set; }

    /// <summary>One of <see cref="MauiFlowEvidenceExpectationStates"/>.</summary>
    [JsonPropertyName("state")] public string? State { get; set; }

    /// <summary>Why the expectation reached that state.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The evidence expectation scopes recorded on a check.</summary>
public static class MauiFlowEvidenceExpectationScopes
{
    public const string Flow = "flow";
    public const string Step = "step";
}
