namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ExtendedFloatingActionButton</c> — the animated
/// extended FAB with separate <c>Icon</c> and <c>Text</c> slots and an
/// <c>Expanded</c> flag that animates between the icon-only and
/// icon&#x202F;+&#x202F;text states.
///
/// <code>
/// new ExtendedFloatingActionButton(onClick: () => count.Value++, expanded: true)
/// {
///     Icon = new Text("+"),
///     Text = new Text("Add"),
/// }
/// </code>
///
/// Both <see cref="Icon"/> and <see cref="Text"/> are required — the
/// underlying Kotlin parameters have no default. Setting either to
/// <c>null</c> throws <see cref="InvalidOperationException"/> at
/// render time.
/// </summary>
public sealed partial class ExtendedFloatingActionButton;
