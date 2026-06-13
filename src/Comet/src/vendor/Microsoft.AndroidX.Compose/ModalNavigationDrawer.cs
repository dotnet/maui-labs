namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ModalNavigationDrawer</c> — a phone-sized drawer that
/// slides over the content with a scrim. The <c>Drawer</c> slot
/// typically holds a <see cref="ModalDrawerSheet"/>; the <c>Content</c>
/// slot holds the screen the drawer overlays. Drag from the left edge
/// or tap the scrim to toggle; pass a <see cref="DrawerStateHolder"/>
/// to programmatically inspect the drawer position. Set
/// <c>ConfirmStateChange</c> to veto a transition (return
/// <c>false</c>) — e.g. block close while a form is dirty.
/// </summary>
public sealed partial class ModalNavigationDrawer
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
