
namespace AndroidX.Compose;

/// <summary>
/// Mutable property bag that mirrors the subset of Kotlin's
/// <c>androidx.navigation.NavOptions.Builder</c> needed by the
/// classic Material 3 bottom-navigation pattern. Pass to
/// <see cref="NavController.Navigate(string, NavOptions)"/> to
/// pop-and-restore back-stack state when switching between top-level
/// tabs:
///
/// <code>
/// nav.Navigate("search", new NavOptions
/// {
///     PopUpToRoute     = "home",   // = startDestinationRoute
///     PopUpToSaveState = true,
///     LaunchSingleTop  = true,
///     RestoreState     = true,
/// });
/// </code>
///
/// <para>
/// Construct with object-initializer syntax — every property is
/// optional and defaults to "behave like the parameterless
/// <see cref="NavController.Navigate(string)"/> overload". Reuse one
/// <see cref="NavOptions"/> instance across navigate calls; the C#
/// peer is built on each call so subsequent mutations are observed.
/// </para>
///
/// <para>
/// Not modelled (yet): <c>Navigator.Extras</c>, integer-id
/// <c>popUpTo</c>, custom enter/exit animations. File an issue if
/// you need them.
/// </para>
/// </summary>
public sealed class NavOptions
{
    /// <summary>
    /// Route at which to apply <c>popUpTo</c> before navigating; pops
    /// every entry above this one off the back stack. Leave
    /// <see langword="null"/> (the default) to skip <c>popUpTo</c>
    /// entirely. The route must already exist on the back stack —
    /// typically the <see cref="NavHost.StartDestination"/>.
    /// </summary>
    public string? PopUpToRoute { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the destination named by
    /// <see cref="PopUpToRoute"/> is itself popped off the back
    /// stack too. Ignored when <see cref="PopUpToRoute"/> is
    /// <see langword="null"/>. Mirrors Kotlin's
    /// <c>popUpTo(route) { inclusive = true }</c>.
    /// </summary>
    public bool PopUpToInclusive { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the popped destinations'
    /// saved state — <c>rememberSaveable</c>, <c>ViewModel</c>s —
    /// is preserved and can be restored later by a navigate call
    /// that also sets <see cref="RestoreState"/>. Standard
    /// bottom-nav pattern: <c>PopUpToSaveState = true</c>. Ignored
    /// when <see cref="PopUpToRoute"/> is <see langword="null"/>.
    /// Mirrors Kotlin's <c>popUpTo(route) { saveState = true }</c>.
    /// </summary>
    public bool PopUpToSaveState { get; set; }

    /// <summary>
    /// When <see langword="true"/>, navigating to a destination
    /// already on top of the back stack is a no-op — preventing a
    /// duplicate entry from being pushed. Mirrors Kotlin's
    /// <c>launchSingleTop = true</c>.
    /// </summary>
    public bool LaunchSingleTop { get; set; }

    /// <summary>
    /// When <see langword="true"/>, any state previously saved for
    /// the target destination (via an earlier
    /// <see cref="PopUpToSaveState"/> pop) is restored when the
    /// destination is re-entered. Mirrors Kotlin's
    /// <c>restoreState = true</c>.
    /// </summary>
    public bool RestoreState { get; set; }

    /// <summary>
    /// Build the Kotlin <c>NavOptions</c> peer by applying every
    /// configured flag to a fresh
    /// <c>androidx.navigation.NavOptions.Builder</c>. Called from
    /// <see cref="NavController.Navigate(string, NavOptions)"/> on
    /// every navigate so mutations to the wrapper between calls are
    /// observed by the next navigate.
    /// </summary>
    internal AndroidX.Navigation.NavOptions BuildJvm()
    {
        // Catch caller bugs early: the route-dependent flags are
        // silently ignored by Kotlin when no popUpTo is set, which
        // is exactly the surprise that bites people writing the
        // bottom-nav pattern (they forget to set PopUpToRoute and
        // wonder why state isn't being saved).
        if (PopUpToRoute is null && (PopUpToInclusive || PopUpToSaveState))
        {
            throw new InvalidOperationException(
                "NavOptions.PopUpToInclusive / PopUpToSaveState require PopUpToRoute to be set; otherwise the popUpTo clause is dropped and the flags have no effect.");
        }

        var builder = new AndroidX.Navigation.NavOptions.Builder();

        if (PopUpToRoute is not null)
            builder.SetPopUpTo(PopUpToRoute, PopUpToInclusive, PopUpToSaveState);

        if (LaunchSingleTop)
            builder.SetLaunchSingleTop(true);

        if (RestoreState)
            builder.SetRestoreState(true);

        return builder.Build();
    }
}
