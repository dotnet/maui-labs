using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>A registered component that can update its visual variant without being remounted.</summary>
public interface ICompositionComponent
{
    string? Variant { get; }

    void ApplyVariant(string? variant);

    void Detach();
}

/// <summary>A persistent app-authored scaffold whose named slots are reconciled in place.</summary>
public interface ICompositionScaffold
{
    string? Title { get; set; }

    IReadOnlyList<View> GetSlotChildren(CompositionSlot slot);

    void ApplySlots(IReadOnlyDictionary<CompositionSlot, IReadOnlyList<View>> slots);
}
