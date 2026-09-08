using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ExpandedFullScreenSearchBar</c> — the full-screen
/// (<c>Dialog</c>-backed) popup half of a <see cref="SearchBar"/> pair.
/// Renders the search-results content (collection-initialized children)
/// inside a fullscreen dialog when the shared <see cref="SearchBarState"/>
/// is expanded.
/// </summary>
/// <remarks>
/// Render BOTH this and a <see cref="SearchBar"/> (or
/// <see cref="TopSearchBar"/>) inside the same parent, sharing the
/// same <see cref="SearchBarState"/> — Compose toggles this popup's
/// visibility internally.
///
/// <see cref="InputField"/> is required and is rendered at the top of
/// the expanded popup; the collection-initialized children render as
/// the result list below it (the underlying Kotlin <c>content</c>
/// lambda receives a <c>ColumnScope</c>).
/// </remarks>
public sealed class ExpandedFullScreenSearchBar : ComposableContainer
{
    readonly SearchBarState _state;

    public ExpandedFullScreenSearchBar(SearchBarState state) => _state = state;

    /// <summary>Required: composable that renders the search input field inside the popup.</summary>
    public ComposableNode? InputField { get; set; }

    public override void Render(IComposer composer)
    {
        if (InputField is null)
            throw new InvalidOperationException(
                "ExpandedFullScreenSearchBar.InputField is required (the Kotlin parameter has no default).");

        var stateHandle = SearchBar.ResolveStateHandle(_state, composer);
        var inputField  = ComposableLambdas.Wrap2(composer, c => InputField.Render(c));
        var content     = ComposableLambdas.Wrap3(composer, c => RenderChildren(c));
        ComposeBridges.ExpandedFullScreenSearchBar(stateHandle, inputField, BuildModifier(), content, composer);
    }
}
