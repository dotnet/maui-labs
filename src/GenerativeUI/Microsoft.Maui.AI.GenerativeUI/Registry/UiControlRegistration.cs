namespace Microsoft.Maui.AI.GenerativeUI.Registry;

/// <summary>
/// A registered custom control exposed as a DSL node <c>type</c>. Created via DI at inflation time;
/// its <see cref="Props"/> arrive through the generic binding tree.
/// See <c>docs/GenerativeUI/spec/appendix-extensibility.md §3.2</c>.
/// </summary>
public sealed class UiControlRegistration
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Type ControlType { get; init; }
    public IReadOnlyList<UiProp> Props { get; init; } = [];
}
