using System.Collections;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Compose Navigation host — the C# moral equivalent of Kotlin's
/// <c>NavHost(navController, startDestination) { composable("a") { ... } }</c>.
/// Holds a graph of <see cref="NavDestination"/> destinations and switches
/// the visible one based on the bound
/// <see cref="AndroidX.Compose.NavController"/>'s back stack.
///
/// <para>
/// Wire a remembered <see cref="AndroidX.Compose.NavController"/> if you
/// want to drive navigation from outside the host (e.g. from button
/// onClick callbacks); leave it <c>null</c> to let the host create
/// its own internal controller via Kotlin's
/// <c>rememberNavController()</c>:
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
///         new Button(onClick: () =&gt; nav.PopBackStack())
///         {
///             new Text("Back"),
///         },
///     },
/// };
/// </code>
///
/// <para>
/// The host requires <c>Xamarin.AndroidX.Navigation.Compose</c>
/// — added transitively when you reference
/// <c>AndroidX.Compose</c>. See dotnet/android-libraries#1444 for
/// the upstream binder bug that requires the JNI bridges in
/// <see cref="ComposeBridges"/>.
/// </para>
/// </summary>
public sealed class NavHost : ComposableNode, IEnumerable
{
    readonly string _startDestination;
    readonly NavController _navController;
    readonly List<NavDestination> _routes = new();

    /// <summary>
    /// Create a navigation host that starts at <paramref name="startDestination"/>.
    /// When <paramref name="navController"/> is <c>null</c>, the host
    /// allocates an internal one — fine for self-contained graphs that
    /// don't need to navigate from outside (e.g. only button onClick
    /// inside destinations).
    /// </summary>
    public NavHost(string startDestination, NavController? navController = null)
    {
        ArgumentNullException.ThrowIfNull(startDestination);
        _startDestination = startDestination;
        _navController    = navController    ?? new NavController();
    }

    /// <summary>The start destination route the host opens to.</summary>
    public string StartDestination => _startDestination;

    /// <summary>The bound navigation controller — never <c>null</c>.</summary>
    public NavController NavController => _navController;

    /// <summary>Add a destination via collection-initializer syntax.</summary>
    public void Add(NavDestination? route)
    {
        if (route is not null)
            _routes.Add(route);
    }

    /// <summary>
    /// Collection-initializer overload that lets callers set
    /// <see cref="ComposableNode.Modifier"/> inline alongside routes.
    /// </summary>
    public void Add(Modifier modifier) => Modifier = modifier;

    IEnumerator IEnumerable.GetEnumerator() => _routes.GetEnumerator();

    public override void Render(IComposer composer)
    {
        // Allocate (or reuse) the underlying NavHostController via
        // Kotlin's rememberNavController(). Because rememberNavController
        // is itself remembered across recompositions, every render gets
        // back the same controller handle — we just rebind the wrapper.
        var controller = ComposeBridges.RememberNavController(composer);
        _navController.Jvm = controller;

        var modifier = BuildModifier();

        // The Kotlin NavHost does
        //   remember(route, startDestination, builder) { navController.createGraph(...) }
        // so the builder's reference identity is part of the graph cache key.
        // If we allocated a fresh NavGraphBuilderLambda on every recomposition,
        // Compose would rebuild the entire graph and reset the back stack to
        // the start destination after every state change. Cache the lambda
        // in the slot table via ComposeExtensions.Remember so its identity is stable.
        //
        // Compose Navigation invokes the builder ONCE (inside createGraph),
        // so the captured 'this' / '_routes' from the first render are the
        // ones registered with the graph. Subsequent recompositions don't
        // re-register routes — that matches Kotlin's behavior where the
        // trailing graph-builder lambda is also captured once.
        var self = this;
        var builder = composer.Remember(() => new NavGraphBuilderLambda(graphBuilder =>
        {
            for (int i = 0; i < self._routes.Count; i++)
                self._routes[i].RegisterInto(graphBuilder);
        }));

        ComposeBridges.NavHost(
            navController:    controller,
            startDestination: _startDestination,
            modifier:         modifier,
            route:            null,
            builder:          builder,
            composer:         composer);
    }
}
