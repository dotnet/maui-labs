namespace Microsoft.Maui.AI.GenerativeUI.Registry;

/// <summary>
/// A light declaration of a control prop or screen input: a model-facing <paramref name="Name"/>,
/// a freeform <paramref name="Description"/> (surfaced verbatim), an optional two-way
/// <paramref name="Editable"/> flag, and a coercion <paramref name="Type"/>.
/// </summary>
public sealed record UiProp(string Name, string Description, bool Editable = false, string Type = "string");
