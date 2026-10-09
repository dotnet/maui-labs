namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="SearchBar"/> /
/// <see cref="TopSearchBar"/> and their paired expanded popups
/// (<see cref="ExpandedDockedSearchBar"/> /
/// <see cref="ExpandedFullScreenSearchBar"/>). The underlying JVM
/// <c>androidx.compose.material3.SearchBarState</c> is created lazily
/// the first time a <see cref="SearchBar"/> bound to this state is
/// rendered.
/// </summary>
/// <remarks>
/// Compose's new state-based SearchBar API splits the always-visible
/// collapsed input bar (<see cref="SearchBar"/>) from the popup with
/// the search results (<see cref="ExpandedFullScreenSearchBar"/> or
/// <see cref="ExpandedDockedSearchBar"/>). Both halves are *always*
/// rendered, referencing the same <see cref="SearchBarState"/>; Compose
/// internally toggles the popup's visibility from the state.
///
/// <code>
/// var search = Remember(() =&gt; new SearchBarState());
/// var input  = Remember(() =&gt; new SearchBarTextFieldState());
///
/// new Box
/// {
///     new SearchBar(state: search)
///     {
///         InputField = new SearchBarInputField(input, search)
///         {
///             Placeholder = new Text("Search"),
///             LeadingIcon = new Text("🔍"),
///         },
///     },
///     new ExpandedFullScreenSearchBar(state: search)
///     {
///         InputField = new SearchBarInputField(input, search),
///         new Text("Result A"),
///         new Text("Result B"),
///     },
/// }
/// </code>
///
/// The wrapper holds the JVM peer (managed-side <c>Java.Lang.Object</c>)
/// so the underlying state object stays alive across recompositions and
/// callbacks. The peer is set the first time a bound <see cref="SearchBar"/>
/// renders.
/// </remarks>
public sealed class SearchBarState
{
    // Holds the bound JVM peer for the androidx.compose.material3.SearchBarState
    // object. Kept as Java.Lang.Object (rather than a binding interface) because
    // the M3 binding doesn't generate an interface for the final SearchBarState
    // class; storing the peer is enough to keep the JNI reference alive and to
    // re-extract the raw handle for subsequent SearchBar / Expanded*SearchBar
    // renders that share this state.
    internal Java.Lang.Object? Jvm;

    /// <summary>
    /// Raw JNI handle of the bound state object, or
    /// <see cref="IntPtr.Zero"/> until the first <see cref="SearchBar"/>
    /// render binds this state.
    /// </summary>
    internal IntPtr Handle => Jvm?.Handle ?? IntPtr.Zero;
}
