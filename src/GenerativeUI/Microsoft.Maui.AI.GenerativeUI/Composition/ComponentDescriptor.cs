namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// Minimal model-facing description of one app-authored composition component.
/// </summary>
public sealed record ComponentDescriptor
{
    public required string Alias { get; init; }
    public required string Description { get; init; }
    public string? WhenNotToUse { get; init; }
    public required string DataContract { get; init; }
    public IReadOnlyList<string> RequiredBindings { get; init; } = [];
    public IReadOnlyList<string> OptionalBindings { get; init; } = [];
    public IReadOnlyList<CompositionSlot> AllowedSlots { get; init; } = [];
    public IReadOnlyList<string> Variants { get; init; } = [];
    public IReadOnlyList<string> AllowedRegions { get; init; } = [];
}
