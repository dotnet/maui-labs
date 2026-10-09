namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>NavigationRail</c>. Vertical analog of
/// <see cref="NavigationBar"/>. Children are <see cref="NavigationRailItem"/>s:
/// <code>
/// new NavigationRail
/// {
///     new NavigationRailItem(selected: tab == 0, onClick: ...) { Icon = ..., Label = ... },
///     new NavigationRailItem(selected: tab == 1, onClick: ...) { Icon = ..., Label = ... },
/// }
/// </code>
/// <c>NavigationRailItem</c> (unlike <see cref="NavigationBarItem"/>) is
/// a top-level static, not a <c>ColumnScope</c> extension, so children
/// render directly without a published scope receiver.
/// </summary>
public sealed partial class NavigationRail;
