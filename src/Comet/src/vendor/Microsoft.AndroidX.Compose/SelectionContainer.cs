namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>SelectionContainer</c> — wraps a subtree so the
/// rendered <see cref="Text"/> nodes inside become user-selectable
/// (long-press to start a selection, drag the handles to extend it,
/// then copy via the system action bar). Without a surrounding
/// <c>SelectionContainer</c>, AndroidX.Compose's <see cref="Text"/> is
/// strictly display-only.
/// <code>
/// new SelectionContainer { new Text("Long-press me to select.") }
/// </code>
/// Nest a <see cref="DisableSelection"/> inside to opt a specific
/// region back out of selection.
/// </summary>
public sealed partial class SelectionContainer;
