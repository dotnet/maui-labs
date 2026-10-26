using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// A durable element selector recorded in a flow test. Exactly one form is meaningful:
/// AutomationId (best) &gt; exact Text &gt; Type+Index (fragile) &gt; raw Id (fragile). Its shape is
/// shared with the Canvas recorder so Canvas-recorded <c>.md</c> tests replay unchanged.
/// </summary>
public sealed class FlowSelector
{
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("typeIndex")] public FlowTypeIndex? TypeIndex { get; set; }

    // The recorder also stamps these on a step's `target` object; tolerate them so a `target`
    // can be read as a selector directly.
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("index")] public int? Index { get; set; }
    [JsonPropertyName("selectorKind")] public string? SelectorKind { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrEmpty(AutomationId) && string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(Id) &&
        TypeIndex is null && !(SelectorKind == "typeIndex" && !string.IsNullOrEmpty(Type) && Index is not null);
}

public sealed class FlowTypeIndex
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("index")] public int Index { get; set; }
}

/// <summary>
/// A recorded assertion. Only <c>Verify == true</c> kinds (<c>propEquals</c>, <c>exists</c>) can
/// fail a step; <c>routeIs</c>/<c>pageChanged</c> are report-only.
/// </summary>
public sealed class FlowAssert
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("selector")] public FlowSelector? Selector { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("expected")] public string? Expected { get; set; }
    [JsonPropertyName("verify")] public bool Verify { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
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

    // scroll args
    [JsonPropertyName("element")] public string? Element { get; set; }
    [JsonPropertyName("dx")] public double? Dx { get; set; }
    [JsonPropertyName("dy")] public double? Dy { get; set; }
    [JsonPropertyName("itemIndex")] public int? ItemIndex { get; set; }
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("animated")] public bool? Animated { get; set; }
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
}

/// <summary>
/// A recorded workflow test: the authoritative machine-readable payload embedded in a
/// <c>.md</c> flow test (the fenced <c>```json maui-test</c> block).
/// </summary>
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
