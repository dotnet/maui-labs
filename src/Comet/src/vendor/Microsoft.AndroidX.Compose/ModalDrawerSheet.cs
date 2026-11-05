namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ModalDrawerSheet</c> — the panel shown by a
/// modal-style navigation drawer. Lays out children as a Column.
/// Typically holds nav items; in this facade any
/// <see cref="ComposableNode"/> works.
///
/// <c>ContainerColor</c> defaults to <c>0L</c>, which the facade
/// resolves to the active
/// <c>MaterialTheme.colorScheme.surfaceContainerLow</c> — matches
/// the upstream Kotlin default
/// (<c>DrawerDefaults.modalContainerColor</c>). Pass any other
/// value to override.
/// </summary>
public sealed partial class ModalDrawerSheet;
