namespace Microsoft.Maui.AI.GenerativeUI.Registry;

/// <summary>
/// A registered style token: a model-facing <see cref="Name"/> mapped to a XAML resource
/// (<see cref="ResourceKey"/>, defaulting to the name), constrained to the control types in
/// <see cref="AppliesTo"/>. See <c>docs/GenerativeUI/spec/appendix-extensibility.md §3.1</c>.
/// </summary>
public sealed class UiStyleRegistration
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Control types the token is valid on (a node matches if it is or derives from one).</summary>
    public IReadOnlyList<string> AppliesTo { get; init; } = [];

    /// <summary>The XAML resource key the token maps to; defaults to <see cref="Name"/>.</summary>
    public string ResourceKey { get; init; } = "";

    /// <summary>True for library-provided base tokens (applied with built-in visual treatment).</summary>
    public bool IsBuiltIn { get; init; }

    public string EffectiveResourceKey => string.IsNullOrEmpty(ResourceKey) ? Name : ResourceKey;
}
