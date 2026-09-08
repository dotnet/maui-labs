using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TopSearchBar</c> (state-based) — variant of
/// <see cref="SearchBar"/> that snaps to a scrolling top app bar
/// position. Same shape as <see cref="SearchBar"/>: provide a
/// <see cref="SearchBarState"/> and an <see cref="InputField"/>; pair
/// with an <see cref="ExpandedFullScreenSearchBar"/> or
/// <see cref="ExpandedDockedSearchBar"/> popup that shares the state.
/// </summary>
public sealed class TopSearchBar : ComposableNode
{
    readonly SearchBarState _state;

    public TopSearchBar(SearchBarState state) => _state = state;

    /// <summary>Required: composable that renders the search input field.</summary>
    public ComposableNode? InputField { get; set; }

    public override void Render(IComposer composer)
    {
        if (InputField is null)
            throw new InvalidOperationException(
                "TopSearchBar.InputField is required (the Kotlin parameter has no default).");

        var stateHandle = SearchBar.ResolveStateHandle(_state, composer);
        var inputField  = ComposableLambdas.Wrap2(composer, c => InputField.Render(c));
        ComposeBridges.TopSearchBar(stateHandle, inputField, BuildModifier(), composer);
    }
}
