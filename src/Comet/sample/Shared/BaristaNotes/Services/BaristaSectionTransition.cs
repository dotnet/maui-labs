#nullable enable
using Comet.Reactive;

namespace CometSamples.BaristaNotes;

public enum BaristaSection
{
    NewDrink,
    Activity,
    Settings,
}

internal static class BaristaSectionTransition
{
    public static void Set(
        Signal<BaristaSection> section,
        Signal<int> sectionIndex,
        BaristaSection value)
    {
        using var hold = ReactiveScheduler.HoldFlushes();
        section.Value = value;
        sectionIndex.Value = (int)value;
    }
}
