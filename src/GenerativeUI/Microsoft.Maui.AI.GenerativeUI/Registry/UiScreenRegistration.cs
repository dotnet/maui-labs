namespace Microsoft.Maui.AI.GenerativeUI.Registry;

/// <summary>
/// A registered full screen the model hands off to (via <c>present_screen</c> or a <c>Screen</c> node).
/// Created via DI; self-loads its own bulk data. The model supplies only declared <see cref="Inputs"/>.
/// See <c>docs/GenerativeUI/spec/appendix-extensibility.md §3.3</c>.
/// </summary>
public sealed class UiScreenRegistration
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Type ScreenType { get; init; }
    public IReadOnlyList<UiProp> Inputs { get; init; } = [];
}
