using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// A durable element selector recorded in a flow test. Exactly one form is meaningful:
/// AutomationId (best) &gt; exact Text &gt; Type+Index (fragile) &gt; raw Id (fragile). Its shape is
/// shared with the Canvas recorder so Canvas-recorded <c>.md</c> tests replay unchanged.
/// </summary>
public sealed class FlowSelector
{
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    /// <summary>Optional app-supplied item identity used with AutomationId inside a repeated collection.</summary>
    [JsonPropertyName("stableItemKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StableItemKey { get; set; }
    /// <summary>AutomationId of the collection that scopes <see cref="StableItemKey"/>.</summary>
    [JsonPropertyName("collectionScope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CollectionScope { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("typeIndex")] public FlowTypeIndex? TypeIndex { get; set; }

    // The recorder also stamps these on a step's `target` object; tolerate them so a `target`
    // can be read as a selector directly.
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("index")] public int? Index { get; set; }
    [JsonPropertyName("selectorKind")] public string? SelectorKind { get; set; }
    // Schema v2 recording diagnostics. They are additive so schema v1 flows remain readable.
    [JsonPropertyName("matchCount")] public int? MatchCount { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("fragilityReasons")] public List<string>? FragilityReasons { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrEmpty(AutomationId) && string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(Id) &&
        TypeIndex is null && !(SelectorKind == "typeIndex" && !string.IsNullOrEmpty(Type) && Index is not null);

    [JsonIgnore]
    public bool HasScopedStableItem =>
        !string.IsNullOrWhiteSpace(AutomationId) &&
        !string.IsNullOrWhiteSpace(StableItemKey) &&
        !string.IsNullOrWhiteSpace(CollectionScope);

    /// <summary>Returns whether this selector is less durable than a unique AutomationId.</summary>
    public static bool IsFragile(FlowSelector? selector)
        => selector is not null
            && !selector.IsEmpty
            && (string.IsNullOrEmpty(selector.AutomationId)
                || selector.MatchCount is > 1
                || string.Equals(selector.Quality, "ambiguous", StringComparison.OrdinalIgnoreCase)
                || selector.FragilityReasons is { Count: > 0 });

    public static bool IsOpaqueStableItemKey(string? value)
        => value is { Length: 71 } &&
           value.StartsWith("sha256:", StringComparison.Ordinal) &&
           value[7..].All(Uri.IsHexDigit);
}

public sealed class FlowTypeIndex
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// A recorded assertion. <c>propEquals</c>, <c>exists</c>, <c>notExists</c>, and <c>routeIs</c> can be verified;
/// <c>pageChanged</c> remains report-only without authoritative before/after page data.
/// </summary>
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

/// <summary>
/// The per-action replay arguments. Heterogeneous by action — only the relevant fields are set.
/// </summary>
public sealed class FlowStepArgs
{
    [JsonPropertyName("selector")] public FlowSelector? Selector { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("valueSource")] public string? ValueSource { get; set; }
    /// <summary>
    /// Environment variable supplying a sensitive value at replay time. Recorded secrets are
    /// never written into the flow or broker spool.
    /// </summary>
    [JsonPropertyName("secretEnvironmentVariable")] public string? SecretEnvironmentVariable { get; set; }

    // scroll args
    [JsonPropertyName("element")] public string? Element { get; set; }
    [JsonPropertyName("dx")] public double? Dx { get; set; }
    [JsonPropertyName("dy")] public double? Dy { get; set; }
    [JsonPropertyName("itemIndex")] public int? ItemIndex { get; set; }
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("animated")] public bool? Animated { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// An artifact category a committed flow declares its run is expected to produce.
/// <para>
/// This is a declaration about <em>collection</em>, not about content. The runner records whether
/// the run produced the category and reports it; it never captures extra evidence because a flow
/// asked for it, and it never compares an artifact against a stored baseline. Nothing here is a
/// golden-image or pixel comparison.
/// </para>
/// </summary>
public sealed class FlowExpectedEvidence
{
    /// <summary>Optional stable identity for this expectation, used to correlate it in the report.</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    /// <summary>One of <see cref="MauiFlowEvidenceKinds"/>.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";

    /// <summary>
    /// The named target for kinds that need one. Required for
    /// <see cref="MauiFlowEvidenceKinds.BusinessOracle"/>, where it is the oracle id.
    /// </summary>
    [JsonPropertyName("reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; set; }

    /// <summary>Why the author expects this evidence. The runner does not interpret this field.</summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class FlowStep
{
    [JsonPropertyName("seq")] public int Seq { get; set; }
    /// <summary>
    /// Optional stable identity for this step. Current recorders assign it once and compatible
    /// readers preserve it; legacy flows continue to use <see cref="Seq"/> as their identity.
    /// Values must be unique, bounded safe identifiers and cannot be numeric sequence aliases.
    /// </summary>
    [JsonPropertyName("stepId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StepId { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    /// <summary>
    /// Human-facing text that describes the intent of this step. It never changes replay behavior.
    /// </summary>
    [JsonPropertyName("label")] public string? Label { get; set; }
    /// <summary>
    /// A concise authoring intent for review. The runner does not interpret this field.
    /// </summary>
    [JsonPropertyName("intent")] public string? Intent { get; set; }
    /// <summary>
    /// Plan acceptance criteria this step helps demonstrate. These links are review metadata only.
    /// </summary>
    [JsonPropertyName("acceptanceCriterionIds")] public List<string>? AcceptanceCriterionIds { get; set; }
    [JsonPropertyName("target")] public FlowSelector? Target { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("args")] public FlowStepArgs? Args { get; set; }
    [JsonPropertyName("page")] public string? Page { get; set; }
    [JsonPropertyName("navigated")] public bool Navigated { get; set; }
    [JsonPropertyName("fragile")] public bool Fragile { get; set; }
    /// <summary>
    /// Legacy shorthand for a step-scoped screenshot expectation. Any non-empty value declares
    /// that the run is expected to produce a screenshot; the value itself names the capture for
    /// review and is never compared against a stored image. Prefer
    /// <see cref="ExpectedEvidence"/> in new flows.
    /// </summary>
    [JsonPropertyName("screenshot")] public string? Screenshot { get; set; }
    /// <summary>
    /// Evidence this step declares the run is expected to produce. Additive: flows without it stay
    /// valid and behave exactly as before.
    /// </summary>
    [JsonPropertyName("expectedEvidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FlowExpectedEvidence>? ExpectedEvidence { get; set; }
    [JsonPropertyName("asserts")] public List<FlowAssert>? Asserts { get; set; }
    /// <summary>
    /// Additive recording evidence for selector-health diagnostics. The active selector remains in
    /// <see cref="Target"/> or <see cref="Args"/> and is never replaced by these candidates.
    /// </summary>
    [JsonPropertyName("selectorEvidence")] public MauiSelectorEvidence? SelectorEvidence { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// A recorded workflow test: the authoritative machine-readable payload embedded in a
/// <c>.md</c> flow test (the fenced <c>```json maui-test</c> block).
/// </summary>
public sealed class MauiFlow
{
    public const int CurrentSchema = 2;

    [JsonPropertyName("schema")] public int Schema { get; set; } = CurrentSchema;
    [JsonPropertyName("name")] public string Name { get; set; } = "scenario";
    [JsonPropertyName("app")] public string? App { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("recordedAt")] public string? RecordedAt { get; set; }
    [JsonPropertyName("preconditions")] public string? Preconditions { get; set; }
    /// <summary>
    /// Evidence this flow declares every run is expected to produce. Additive: flows without it
    /// stay valid and behave exactly as before.
    /// </summary>
    [JsonPropertyName("expectedEvidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FlowExpectedEvidence>? ExpectedEvidence { get; set; }
    [JsonPropertyName("steps")] public List<FlowStep> Steps { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The set of mutating actions a recorded flow step may drive.</summary>
public static class FlowActions
{
    public const string Tap = "tap";
    public const string Fill = "fill";
    public const string Scroll = "scroll";
    public const string Navigate = "navigate";
    public const string Back = "back";
    public const string SetTheme = "setTheme";
    public const string SetProperty = "setProperty";
    // A validation-only step: it drives nothing, it just carries assertions that run at this point in
    // the sequence (enables asserting the initial state, before any action). Recorded interactively
    // via the inspector's "Assert" affordance.
    public const string Assert = "assert";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Tap, Fill, Scroll, Navigate, Back, SetTheme, SetProperty, Assert,
    };
}

public static class FlowSecretReference
{
    public const string EnvironmentPrefix = "MAUI_DEVFLOW_SECRET_";

    private static readonly string[] SensitiveFragments =
    [
        "password", "passcode", "secret", "token", "apikey", "api_key", "api-key",
        "credential", "authorization", "cookie", "pwd", "privatekey", "private_key", "private-key",
        "accesskey", "access_key", "access-key", "signingkey", "signing_key", "signing-key",
        "clientsecret", "client_secret", "client-secret", "refreshtoken", "refresh_token",
        "refresh-token"
    ];

    public static bool LooksSensitive(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            var normalized = value.Trim().ToLowerInvariant();
            var compact = new string(normalized.Where(char.IsAsciiLetterOrDigit).ToArray());
            if (SensitiveFragments.Any(normalized.Contains) ||
                new[] { "pin", "otp", "cvv", "ssn", "mfa" }.Any(
                    marker => compact.StartsWith(marker, StringComparison.Ordinal) ||
                              compact.EndsWith(marker, StringComparison.Ordinal)))
            {
                return true;
            }
        }
        return false;
    }

    public static string BuildEnvironmentVariable(
        string? automationId,
        string? propertyName,
        string? type,
        int sequence)
    {
        var hint = new[] { automationId, propertyName, type }
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var suffix = new string((hint ?? $"STEP_{sequence}")
            .ToUpperInvariant()
            .Select(static character => char.IsAsciiLetterOrDigit(character) ? character : '_')
            .ToArray())
            .Trim('_');
        if (string.IsNullOrEmpty(suffix))
            suffix = $"STEP_{sequence}";
        suffix = suffix[..Math.Min(suffix.Length, 56)];
        return $"{EnvironmentPrefix}{suffix}_STEP_{sequence}";
    }

    public static bool IsValidEnvironmentVariable(string? value)
        => value is { Length: > 0 and <= 96 } &&
           value.StartsWith(EnvironmentPrefix, StringComparison.Ordinal) &&
           value.All(static character => character == '_' || char.IsAsciiLetterOrDigit(character));
}
