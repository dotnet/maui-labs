using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

[JsonConverter(typeof(JsonStringEnumConverter<CompositionSlot>))]
public enum CompositionSlot
{
    Hero,
    Primary,
    Supporting,
    Actions,
}

public sealed record CompositionPlan
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string PlanId { get; init; }
    public required int Revision { get; init; }
    public required string Scaffold { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<CompositionSection> Sections { get; init; } = [];
}

public sealed record CompositionSection
{
    public required string Id { get; init; }
    public required CompositionSlot Slot { get; init; }
    public required string Component { get; init; }
    public required string DataPath { get; init; }
    public string? Variant { get; init; }
    public required int Priority { get; init; }
    public required string Reason { get; init; }
}

public sealed record CompositionSlotDescriptor(CompositionSlot Slot, bool AllowsMultiple);
