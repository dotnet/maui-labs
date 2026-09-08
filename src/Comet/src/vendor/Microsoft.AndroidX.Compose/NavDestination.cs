using System.Collections;
using Android.Runtime;
using AndroidX.Navigation;

namespace AndroidX.Compose;

/// <summary>
/// A single destination registered with a <see cref="NavHost"/>. The
/// route string is the unique key the host uses to match
/// <see cref="NavController.Navigate(string)"/> calls; the body is
/// the @Composable content shown while this destination sits on top
/// of the back stack.
///
/// <para>
/// Two construction shapes — collection-init for static content,
/// or a <c>Func&lt;NavBackStackEntry, ComposableNode&gt;</c> for
/// dynamic content that needs to read route arguments:
/// </para>
/// <code>
/// // Static — same children every time the route is shown
/// new NavDestination("home")
/// {
///     new Text("Home"),
/// }
///
/// // Dynamic — read the {id} placeholder from the back-stack entry
/// new NavDestination("user/{id}", entry =&gt;
/// {
///     var id = entry.Arguments?.GetString("id") ?? "?";
///     return new Text($"User #{id}");
/// })
/// </code>
///
/// <para>
/// Mirrors Kotlin's <c>NavGraphBuilder.composable(route) { backStackEntry -&gt; ... }</c>.
/// Named <c>NavDestination</c> rather than <c>Composable</c> to avoid
/// confusion with the unrelated <see cref="ComposableNode"/> base class
/// and Kotlin's <c>@Composable</c> annotation — this type isn't a UI
/// node, it's a route registration.
/// Add instances to a <see cref="NavHost"/> via collection-initializer
/// — they're not normal <see cref="ComposableNode"/>s, since their
/// content is composed inside the NavHost's per-route subcomposition,
/// not the surrounding tree.
/// </para>
/// </summary>
public sealed class NavDestination : IEnumerable
{
    readonly List<ComposableNode> _staticChildren = new();
    readonly Func<NavBackStackEntry, ComposableNode>? _factory;

    /// <summary>
    /// Register a destination with a static child tree. Add children
    /// via collection-initializer syntax.
    /// </summary>
    public NavDestination(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        Route = route;
    }

    /// <summary>
    /// Register a destination whose content depends on the
    /// <see cref="NavBackStackEntry"/> — typically used to read
    /// route placeholders (e.g. <c>{id}</c> in <c>"user/{id}"</c>)
    /// from the entry's <see cref="NavBackStackEntry.Arguments"/>.
    /// </summary>
    public NavDestination(string route, Func<NavBackStackEntry, ComposableNode> content)
    {
        ArgumentNullException.ThrowIfNull(route);
        Route    = route;
        ArgumentNullException.ThrowIfNull(content);
        _factory = content;
    }

    /// <summary>The route string used to navigate to this destination.</summary>
    public string Route { get; }

    /// <summary>
    /// Add a child to the static content tree. Required by C#'s
    /// collection-initializer syntax. Calling this on a destination
    /// constructed with the dynamic-content factory throws.
    /// </summary>
    public void Add(ComposableNode? child)
    {
        if (_factory is not null)
            throw new InvalidOperationException(
                "NavDestination was constructed with a dynamic content factory; collection-init children are not supported.");
        if (child is not null)
            _staticChildren.Add(child);
    }

    /// <summary>
    /// Collection-initializer overload that exists only to give a clear
    /// error message — Kotlin's <c>composable(route) { ... }</c> has no
    /// per-destination <c>Modifier</c> slot, so silently accepting one
    /// would be misleading. Wrap the destination's content in a
    /// <see cref="Box"/> / <see cref="Column"/> / <see cref="Row"/> with
    /// the modifier, or set the modifier on the surrounding
    /// <see cref="NavHost"/> instead.
    /// </summary>
    public void Add(Modifier modifier) =>
        throw new InvalidOperationException(
            "Compose Navigation's composable() route has no Modifier slot. " +
            "Apply the Modifier to a wrapping Box/Column/Row inside the destination, " +
            "or to the surrounding NavHost.");

    IEnumerator IEnumerable.GetEnumerator() => _staticChildren.GetEnumerator();

    // Register this route into the surrounding NavHost's NavGraphBuilder.
    // Called once per NavDestination per NavHost composition pass — Kotlin
    // composable() runs synchronously inside the builder lambda, so the
    // outer NavHost composer is still active here. The destination's
    // content lambda, however, is invoked LATER inside the route's own
    // subcomposition (every time the user navigates here), so the
    // content must be wrapped via ComposableLambdaInstance, not
    // ComposableLambda — see ComposableLambdas.InstantiateNavComposable.
    internal void RegisterInto(NavGraphBuilder graphBuilder)
    {
        var content = ComposableLambdas.InstantiateNavComposable((entryHandle, destComposer) =>
        {
            // Render either the dynamic factory's result or the static
            // children inside the destination's own composer.
            if (_factory is not null)
            {
                var entry = entryHandle == IntPtr.Zero
                    ? null
                    : Java.Lang.Object.GetObject<AndroidX.Navigation.NavBackStackEntry>(
                        entryHandle, JniHandleOwnership.DoNotTransfer);
                var wrapper = entry is null ? null : new NavBackStackEntry(entry);
                if (wrapper is null)
                    throw new InvalidOperationException(
                        "Compose Navigation invoked a destination with a null back-stack entry; this should never happen.");
                _factory(wrapper).Render(destComposer);
            }
            else
            {
                for (int i = 0; i < _staticChildren.Count; i++)
                {
                    destComposer.StartReplaceableGroup(i);
                    try { _staticChildren[i].Render(destComposer); }
                    finally { destComposer.EndReplaceableGroup(); }
                }
            }
        });

        ComposeBridges.NavGraphBuilderComposable(
            navGraphBuilder: ((Java.Lang.Object)graphBuilder).Handle,
            route:           Route,
            arguments:       null,
            deepLinks:       null,
            content:         content);
    }
}
