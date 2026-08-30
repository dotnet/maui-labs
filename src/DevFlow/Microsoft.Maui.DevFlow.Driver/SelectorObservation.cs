using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// A value-free snapshot captured while a workflow recording observes a target. It deliberately
/// excludes rendered text, control values, screenshots, and runtime object references.
/// </summary>
public sealed class MauiSelectorObservation
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("target")] public MauiSelectorObservationElement? Target { get; set; }
    [JsonPropertyName("elements")] public List<MauiSelectorObservationElement> Elements { get; set; } = [];
    [JsonPropertyName("context")] public MauiSelectorObservationContext? Context { get; set; }
    [JsonPropertyName("truncated")] public bool? Truncated { get; set; }
}

/// <summary>
/// Structural target facts used by selector-health tooling. These are not an executable selector.
/// </summary>
public sealed class MauiSelectorObservationElement
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

/// <summary>
/// App and display facts accompanying a value-free recording observation.
/// </summary>
public sealed class MauiSelectorObservationContext
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
