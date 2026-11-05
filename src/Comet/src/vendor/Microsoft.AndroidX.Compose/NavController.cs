
namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied wrapper around an
/// <c>androidx.navigation.NavHostController</c>. Pass an instance to
/// <see cref="NavHost"/> and it will be populated with the underlying
/// Kotlin controller on first composition (via Kotlin's
/// <c>rememberNavController()</c>); from then on the navigation methods
/// — <see cref="Navigate(string)"/>, <c>PopBackStack</c>,
/// <see cref="NavigateUp"/> — forward straight to the bound
/// <see cref="AndroidX.Navigation.NavController"/> binding.
///
/// <para>
/// Hold a single instance per logical navigation graph (typically one
/// per <see cref="NavHost"/>) inside a
/// <c>composer.Remember</c> callback so the controller
/// survives recompositions:
/// </para>
/// <code>
/// var nav = Remember(() =&gt; new NavController());
///
/// new NavHost(startDestination: "home", navController: nav)
/// {
///     new NavDestination("home")
///     {
///         new Button(onClick: () =&gt; nav.Navigate("detail"))
///         {
///             new Text("Go to detail"),
///         },
///     },
///     new NavDestination("detail")
///     {
///         new Text("Detail screen"),
///     },
/// };
/// </code>
/// </summary>
public sealed class NavController
{
    /// <summary>
    /// The underlying Kotlin <c>NavHostController</c>. Populated by
    /// <see cref="NavHost.Render"/> on first composition; <c>null</c>
    /// before the host has rendered. Exposed as <c>internal</c> so the
    /// facade can stamp it; user code reaches the controller through
    /// the <see cref="Navigate(string)"/> / <see cref="PopBackStack()"/> /
    /// <see cref="NavigateUp"/> methods.
    /// </summary>
    internal AndroidX.Navigation.NavHostController? Jvm { get; set; }

    AndroidX.Navigation.NavHostController EnsureJvm() =>
        Jvm ?? throw new InvalidOperationException(
            "NavController has not been bound to a NavHost yet. Pass it to a NavHost and wait for the first composition before calling navigation methods.");

    /// <summary>
    /// Navigate to <paramref name="route"/>. Equivalent to
    /// Kotlin's <c>navController.navigate(route)</c>. The route must
    /// be registered via a <see cref="NavDestination"/> child of the
    /// owning <see cref="NavHost"/>.
    /// </summary>
    public void Navigate(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        EnsureJvm().Navigate(route);
    }

    /// <summary>
    /// Navigate to <paramref name="route"/> with the back-stack
    /// behaviour described by <paramref name="options"/> — the
    /// C# equivalent of Kotlin's
    /// <c>navController.navigate(route, navOptions { ... })</c>.
    /// Used to implement the standard Material 3 bottom-navigation
    /// pattern (<c>popUpTo(startDestinationRoute) { saveState = true }</c>
    /// + <c>launchSingleTop = true</c> + <c>restoreState = true</c>):
    ///
    /// <code>
    /// nav.Navigate("search", new NavOptions
    /// {
    ///     PopUpToRoute     = "home",
    ///     PopUpToSaveState = true,
    ///     LaunchSingleTop  = true,
    ///     RestoreState     = true,
    /// });
    /// </code>
    /// </summary>
    public void Navigate(string route, NavOptions options)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(options);
        EnsureJvm().Navigate(route, options.BuildJvm());
    }

    /// <summary>
    /// Pop the most recent destination off the back stack. Returns
    /// <c>true</c> when a destination was popped, <c>false</c> when
    /// the stack was already empty. Mirrors Kotlin's
    /// <c>navController.popBackStack()</c>.
    /// </summary>
    public bool PopBackStack() => EnsureJvm().PopBackStack();

    /// <summary>
    /// Pop until the given <paramref name="route"/> is on top of the
    /// back stack. When <paramref name="inclusive"/> is <c>true</c>,
    /// the route itself is also popped. Mirrors Kotlin's
    /// <c>navController.popBackStack(route, inclusive)</c>.
    /// </summary>
    public bool PopBackStack(string route, bool inclusive)
    {
        ArgumentNullException.ThrowIfNull(route);
        return EnsureJvm().PopBackStack(route, inclusive);
    }

    /// <summary>
    /// Navigate up to the previous destination — equivalent to
    /// pressing the system Up button. Returns <c>true</c> when the up
    /// navigation was handled, <c>false</c> otherwise. Mirrors
    /// Kotlin's <c>navController.navigateUp()</c>.
    /// </summary>
    public bool NavigateUp() => EnsureJvm().NavigateUp();

    /// <summary>
    /// The current top-of-stack <see cref="NavBackStackEntry"/>, or
    /// <c>null</c> while the host is still resolving its start
    /// destination. Mirrors Kotlin's
    /// <c>navController.currentBackStackEntry</c>.
    /// </summary>
    public NavBackStackEntry? CurrentBackStackEntry
    {
        get
        {
            var jvm = EnsureJvm().CurrentBackStackEntry;
            return jvm is null ? null : new NavBackStackEntry(jvm);
        }
    }
}
