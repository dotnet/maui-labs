using AndroidX.Compose.Foundation.Text.Input;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for the text inside a
/// <see cref="SearchBarInputField"/>. Mirrors <see cref="SearchBarState"/>:
/// the underlying JVM
/// <c>androidx.compose.foundation.text.input.TextFieldState</c> is created
/// lazily the first time a <see cref="SearchBarInputField"/> bound to this
/// state is rendered, via Compose's <c>rememberTextFieldState</c>.
/// </summary>
/// <remarks>
/// Both halves of the SearchBar pair — the collapsed
/// <see cref="SearchBar"/> and the expanded popup
/// (<see cref="ExpandedFullScreenSearchBar"/> /
/// <see cref="ExpandedDockedSearchBar"/>) — pass the SAME
/// <see cref="SearchBarTextFieldState"/> to their input fields so the
/// typed text is shared between them.
///
/// <para>Read <see cref="Text"/> inside composition to filter a result
/// list; it goes through to the live JVM peer's <c>text</c> property
/// (which is itself a Compose snapshot state), so typing in the field
/// triggers recomposition.</para>
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
///             Placeholder = new Text("Search fruits"),
///             LeadingIcon = new Text("🔍"),
///         },
///     },
///     new ExpandedFullScreenSearchBar(state: search)
///     {
///         InputField = new SearchBarInputField(input, search),
///         new Text($"Matches: {input.Text}"),
///     },
/// }
/// </code>
/// </remarks>
public sealed class SearchBarTextFieldState
{
    readonly string _initialText;

    // Bound peer for the JVM androidx.compose.foundation.text.input.TextFieldState.
    // Set on first SearchBarInputField render and reused by every subsequent
    // render so collapsed + expanded halves share one TextFieldState.
    internal TextFieldState? Jvm;

    /// <summary>
    /// Creates a state holder with an optional initial text value
    /// (defaults to the empty string).
    /// </summary>
    public SearchBarTextFieldState(string initialText = "") =>
        _initialText = initialText;

    /// <summary>
    /// Current text in the search input. Reads through to the live
    /// JVM <c>TextFieldState.text</c> (a Compose snapshot value), so
    /// reading this inside composition subscribes to recomposition.
    /// Returns the initial value before the first render binds the peer.
    /// </summary>
    public string Text => Jvm?.Text ?? _initialText;

    // Lazy-resolve the bound JVM peer. Multiple SearchBarInputField
    // siblings sharing this state hit the JNI bridge once on the FIRST
    // render and reuse the peer for every subsequent half.
    internal TextFieldState Resolve(IComposer composer)
    {
        if (Jvm is not null)
            return Jvm;

        // Bit 1 = default initialSelection (Kotlin places the cursor at
        // the end of initialText). initialText is provided explicitly.
        Jvm = TextFieldStateKt.RememberTextFieldState(_initialText, 0L, composer, 0, 2);
        return Jvm;
    }
}
