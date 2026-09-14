using Android.Runtime;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>SearchBar</c> (state-based). The always-visible
/// collapsed input field that pairs with an
/// <see cref="ExpandedFullScreenSearchBar"/> or
/// <see cref="ExpandedDockedSearchBar"/> popup sharing the same
/// <see cref="SearchBarState"/>.
/// </summary>
/// <remarks>
/// <see cref="InputField"/> is required (the Kotlin parameter has no
/// default). Use a <see cref="SearchBarInputField"/> so the input
/// field can wire focus / click events to the
/// <see cref="SearchBarState"/> internally — a bare
/// <see cref="TextField"/> renders but does not expand the
/// popup on tap.
///
/// <code>
/// var search = Remember(() =&gt; new SearchBarState());
/// var input  = Remember(() =&gt; new SearchBarTextFieldState());
///
/// new SearchBar(state: search)
/// {
///     InputField = new SearchBarInputField(input, search)
///     {
///         Placeholder = new Text("Search"),
///         LeadingIcon = new Text("🔍"),
///     },
/// }
/// </code>
/// </remarks>
public sealed class SearchBar : ComposableNode
{
    readonly SearchBarState _state;

    public SearchBar(SearchBarState state) => _state = state;

    /// <summary>Required: composable that renders the search input field.</summary>
    public ComposableNode? InputField { get; set; }

    public override void Render(IComposer composer)
    {
        if (InputField is null)
            throw new InvalidOperationException(
                "SearchBar.InputField is required (the Kotlin parameter has no default).");

        var stateHandle = ResolveStateHandle(_state, composer);
        var inputField  = ComposableLambdas.Wrap2(composer, c => InputField.Render(c));
        ComposeBridges.SearchBar(stateHandle, inputField, BuildModifier(), composer);
    }

    // Lazy-resolve the bound JVM peer for the shared SearchBarState. Compose's
    // remember slots are positional, so calling rememberSearchBarState from
    // each facade (collapsed bar + expanded popup) would allocate TWO
    // independent SearchBarState peers — breaking the shared-state design.
    // Cache the peer on the C# wrapper so only the FIRST half to render
    // invokes the JNI bridge; the other half reuses the cached handle.
    // Shared with TopSearchBar, ExpandedDockedSearchBar,
    // ExpandedFullScreenSearchBar.
    internal static IntPtr ResolveStateHandle(SearchBarState state, IComposer composer)
    {
        if (state.Jvm is not null)
            return state.Jvm.Handle;

        var handle = ComposeBridges.RememberSearchBarState(composer);
        state.Jvm = Java.Lang.Object.GetObject<Java.Lang.Object>(handle, JniHandleOwnership.DoNotTransfer)!;
        return handle;
    }
}
