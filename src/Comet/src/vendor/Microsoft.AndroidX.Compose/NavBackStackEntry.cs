namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around an
/// <c>androidx.navigation.NavBackStackEntry</c> — represents a single
/// destination on the navigation back stack. Each
/// <c>composable("route") { ... }</c> destination receives one when
/// composed: it carries the parsed route arguments (path
/// placeholders and query strings) plus the matched
/// <c>NavDestination</c>.
///
/// <para>
/// Usage with the dynamic-content overload of <see cref="NavDestination"/>:
/// </para>
/// <code>
/// new NavDestination("user/{id}", entry =&gt;
/// {
///     var id = entry.Arguments?.GetString("id") ?? "?";
///     return new Text($"User #{id}");
/// });
/// </code>
/// </summary>
public sealed class NavBackStackEntry
{
    /// <summary>
    /// The underlying Kotlin <c>NavBackStackEntry</c>.
    /// </summary>
    internal AndroidX.Navigation.NavBackStackEntry Jvm { get; }

    internal NavBackStackEntry(AndroidX.Navigation.NavBackStackEntry jvm)
    {
        ArgumentNullException.ThrowIfNull(jvm);
        Jvm = jvm;
    }

    /// <summary>
    /// Arguments parsed for this entry — both path placeholders (e.g.
    /// <c>"user/{id}"</c> populates <c>"id"</c>) and any explicit
    /// arguments configured on the destination. <c>null</c> until the
    /// route has been resolved. Mirrors Kotlin's
    /// <c>NavBackStackEntry.arguments</c>.
    /// </summary>
    public Bundle? Arguments => Jvm.Arguments;

    /// <summary>
    /// Route registered for this destination — the same string the
    /// caller passed to <see cref="NavDestination(string)"/>.
    /// <c>null</c> if the destination was registered without a route
    /// (e.g. by an integer id graph). Mirrors Kotlin's
    /// <c>NavBackStackEntry.destination.route</c>.
    /// </summary>
    public string? Route => Jvm.Destination?.Route;
}
