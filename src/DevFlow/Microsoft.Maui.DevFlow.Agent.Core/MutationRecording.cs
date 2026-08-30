using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal sealed class MutationRecordingRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "status";
    [JsonPropertyName("leaseId")]
    public string? LeaseId { get; set; }
    [JsonPropertyName("recordingId")]
    public string? RecordingId { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("app")]
    public string? App { get; set; }
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
    [JsonPropertyName("preconditions")]
    public string? Preconditions { get; set; }
    [JsonPropertyName("observation")]
    public MutationObservation? Observation { get; set; }
}

internal sealed class MutationRecordingStatus
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }
    [JsonPropertyName("recording")]
    public bool Recording { get; set; }
    [JsonPropertyName("recordingId")]
    public string? RecordingId { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("steps")]
    public int Steps { get; set; }
    [JsonPropertyName("seq")]
    public int? Seq { get; set; }
    [JsonPropertyName("fragile")]
    public bool Fragile { get; set; }
    [JsonPropertyName("empty")]
    public bool Empty { get; set; }
    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }
    [JsonPropertyName("warnings")]
    public string[]? Warnings { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

internal sealed class MutationObservation
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";
    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }
    [JsonPropertyName("text")]
    public string? Text { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("index")]
    public int? Index { get; set; }
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("value")]
    public string? Value { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("dx")]
    public double? Dx { get; set; }
    [JsonPropertyName("dy")]
    public double? Dy { get; set; }
    [JsonPropertyName("itemIndex")]
    public int? ItemIndex { get; set; }
    [JsonPropertyName("position")]
    public string? Position { get; set; }
    [JsonPropertyName("page")]
    public string? Page { get; set; }
    [JsonPropertyName("navigated")]
    public bool Navigated { get; set; }
    [JsonPropertyName("assertsJson")]
    public string? AssertsJson { get; set; }
    [JsonPropertyName("matchCount")]
    public int? MatchCount { get; set; }
    [JsonPropertyName("quality")]
    public string? Quality { get; set; }
    [JsonPropertyName("fragilityReasons")]
    public string[]? FragilityReasons { get; set; }
    [JsonPropertyName("valueSource")]
    public string? ValueSource { get; set; }
    [JsonPropertyName("sensitive")]
    public bool Sensitive { get; set; }
    [JsonPropertyName("selectorObservation")]
    public SelectorObservationPayload? SelectorObservation { get; set; }
}

/// <summary>
/// Value-free selector facts sent to the broker with an observed mutation. This deliberately
/// mirrors the Driver DTO by JSON shape without making Agent.Core depend on Testing or Driver.
/// </summary>
internal sealed class SelectorObservationPayload
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("target")] public SelectorObservationElement? Target { get; set; }
    [JsonPropertyName("elements")] public List<SelectorObservationElement> Elements { get; set; } = [];
    [JsonPropertyName("context")] public SelectorObservationContext? Context { get; set; }
    [JsonPropertyName("truncated")] public bool? Truncated { get; set; }
}

internal sealed class SelectorObservationElement
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("parentId")] public string? ParentId { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("fullType")] public string? FullType { get; set; }
    [JsonPropertyName("framework")] public string? Framework { get; set; }
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    [JsonPropertyName("nativeAutomationIdentity")] public string? NativeAutomationIdentity { get; set; }
    [JsonPropertyName("nativeAutomationIdentityKind")] public string? NativeAutomationIdentityKind { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("traits")] public List<string>? Traits { get; set; }
    [JsonPropertyName("isVisible")] public bool IsVisible { get; set; }
    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
    [JsonPropertyName("isFocused")] public bool IsFocused { get; set; }
    [JsonPropertyName("bounds")] public BoundsInfo? Bounds { get; set; }
    [JsonPropertyName("windowBounds")] public BoundsInfo? WindowBounds { get; set; }
    [JsonPropertyName("sourceFile")] public string? SourceFile { get; set; }
    [JsonPropertyName("sourceLine")] public int? SourceLine { get; set; }
    [JsonPropertyName("sourceColumn")] public int? SourceColumn { get; set; }
    [JsonPropertyName("sourceHash")] public string? SourceHash { get; set; }
    [JsonPropertyName("sourceConfidence")] public string? SourceConfidence { get; set; }
    [JsonPropertyName("stableItemKey")] public string? StableItemKey { get; set; }
    [JsonPropertyName("collectionScope")] public string? CollectionScope { get; set; }
    [JsonPropertyName("templateKind")] public string? TemplateKind { get; set; }
    [JsonPropertyName("isVirtualized")] public bool? IsVirtualized { get; set; }
}

internal sealed class SelectorObservationContext
{
    [JsonPropertyName("appId")] public string? AppId { get; set; }
    [JsonPropertyName("appBuild")] public string? AppBuild { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("modal")] public string? Modal { get; set; }
    [JsonPropertyName("locale")] public string? Locale { get; set; }
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("orientation")] public string? Orientation { get; set; }
    [JsonPropertyName("displayProfile")] public string? DisplayProfile { get; set; }
    [JsonPropertyName("capabilityVersion")] public string? CapabilityVersion { get; set; }
    [JsonPropertyName("observedAt")] public DateTimeOffset? ObservedAt { get; set; }
}

internal sealed class MutationRecordingTracker
{
    private readonly object _gate = new();
    private bool _active;
    private string? _recordingId;

    public bool IsActive
    {
        get { lock (_gate) return _active; }
    }

    public string? RecordingId
    {
        get { lock (_gate) return _recordingId; }
    }

    public void Update(MutationRecordingStatus? status)
    {
        if (status?.Ok != true)
            return;
        lock (_gate)
        {
            _active = status.Recording;
            _recordingId = status.Recording ? status.RecordingId : null;
        }
    }
}
