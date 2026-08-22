using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Registry;

public sealed class UiScaffoldRegistration
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Type ScaffoldType { get; init; }
    public IReadOnlyList<CompositionSlotDescriptor> Slots { get; init; } = [];
}
