using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// The Material 3 search input field that drives the state-based
/// <see cref="SearchBar"/> family. Plug into the <c>InputField</c> slot
/// of <see cref="SearchBar"/>, <see cref="TopSearchBar"/>,
/// <see cref="ExpandedDockedSearchBar"/>, or
/// <see cref="ExpandedFullScreenSearchBar"/>; the shared
/// <see cref="SearchBarState"/> + <see cref="SearchBarTextFieldState"/>
/// wire focus / click events to the popup's expand/collapse animations
/// internally.
/// </summary>
/// <remarks>
/// Compose Kotlin source: <c>SearchBarDefaults.InputField(textFieldState,
/// searchBarState, …)</c>. Without this facade a bare
/// <see cref="TextField"/> placed in the input-field slot
/// will not know how to talk to <see cref="SearchBarState"/> — the bar
/// would render but never expand.
///
/// <code>
/// var search = Remember(() =&gt; new SearchBarState());
/// var input  = Remember(() =&gt; new SearchBarTextFieldState());
///
/// new SearchBar(state: search)
/// {
///     InputField = new SearchBarInputField(input, search)
///     {
///         OnSearch    = q =&gt; Log.Info("search", q),
///         Placeholder = new Text("Search fruits"),
///         LeadingIcon = new Text("🔍"),
///     },
/// }
/// </code>
/// </remarks>
public sealed class SearchBarInputField : ComposableNode
{
    readonly SearchBarTextFieldState _textState;
    readonly SearchBarState          _searchState;

    /// <summary>
    /// Creates the input field bound to a shared
    /// <see cref="SearchBarTextFieldState"/> (the typed text) and
    /// <see cref="SearchBarState"/> (the expand/collapse state). Pass
    /// the same two state objects to every <see cref="SearchBar"/> /
    /// expanded popup half so they share text + open state.
    /// </summary>
    public SearchBarInputField(SearchBarTextFieldState textState, SearchBarState searchState)
    {
        _textState   = textState;
        _searchState = searchState;
    }

    /// <summary>
    /// Optional callback invoked when the user presses the IME Search
    /// action. Receives the current text. If omitted, a no-op callback
    /// is supplied — required by the Kotlin API which does not tolerate
    /// a null <c>onSearch</c>.
    /// </summary>
    public Action<string>? OnSearch { get; set; }

    /// <summary>
    /// Optional placeholder rendered when the field is empty
    /// (Compose Kotlin <c>placeholder</c> slot).
    /// </summary>
    public ComposableNode? Placeholder { get; set; }

    /// <summary>
    /// Optional leading icon, typically a magnifying-glass glyph
    /// (Compose Kotlin <c>leadingIcon</c> slot). No icon is drawn when
    /// omitted — the bar still functions but loses the canonical M3
    /// search-bar look.
    /// </summary>
    public ComposableNode? LeadingIcon { get; set; }

    /// <summary>
    /// Optional trailing icon, typically a clear/close button shown
    /// when the popup is expanded (Compose Kotlin <c>trailingIcon</c>
    /// slot).
    /// </summary>
    public ComposableNode? TrailingIcon { get; set; }

    public override void Render(IComposer composer)
    {
        var textPeer     = _textState.Resolve(composer);
        var searchHandle = SearchBar.ResolveStateHandle(_searchState, composer);

        var onSearch = OnSearch is null
            ? (Kotlin.Jvm.Functions.IFunction1)NoOpSearchCallback.Instance
            : new ComposableLambda1(p => OnSearch(p?.ToString() ?? ""));

        var placeholder  = Placeholder  is null ? null : ComposableLambdas.Wrap2(composer, c => Placeholder.Render(c));
        var leadingIcon  = LeadingIcon  is null ? null : ComposableLambdas.Wrap2(composer, c => LeadingIcon.Render(c));
        var trailingIcon = TrailingIcon is null ? null : ComposableLambdas.Wrap2(composer, c => TrailingIcon.Render(c));

        ComposeBridges.SearchBarDefaultsInputField(
            textPeer.Handle,
            searchHandle,
            onSearch,
            BuildModifier(),
            placeholder,
            leadingIcon,
            trailingIcon,
            composer);
    }
}
