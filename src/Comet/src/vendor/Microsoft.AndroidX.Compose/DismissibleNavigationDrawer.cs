namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>DismissibleNavigationDrawer</c> — a drawer that
/// pushes the main content aside when open (no scrim, no overlay).
/// The <c>Drawer</c> slot typically holds a
/// <see cref="DismissibleDrawerSheet"/>. Drag horizontally to toggle.
/// Behavior mirrors <see cref="ModalNavigationDrawer"/> for the state
/// holder and <c>ConfirmStateChange</c> veto.
/// </summary>
public sealed partial class DismissibleNavigationDrawer
{
    /// <summary>
    /// Convenience: starting <see cref="AndroidX.Compose.Material3.DrawerValue"/>
    /// on first composition. Mirrors Kotlin
    /// <c>rememberDrawerState(initialValue = DrawerValue.Open)</c>.
    /// </summary>
    /// <remarks>
    /// Setting this constructs a default <see cref="DrawerStateHolder"/>
    /// with the requested initial value. If the caller also passes an
    /// explicit holder to the constructor, the init setter overwrites
    /// it — pass the holder directly (with the desired
    /// <c>InitialValue</c>) when you need to share state across
    /// recompositions.
    /// </remarks>
    public AndroidX.Compose.Material3.DrawerValue? InitialValue
    {
        get => _drawerState?.InitialValue;
        init => _drawerState = new DrawerStateHolder(value);
    }
}
