using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>DockedSearchBar</c>. Two deprecated overloads share this
/// class:
/// <list type="bullet">
///   <item><description>
///     <see cref="DockedSearchBar(bool, Action{bool})"/> — the
///     boolean-state variant (M3 1.2+ form: caller supplies an
///     <see cref="InputField"/> slot and toggles <c>expanded</c>).
///   </description></item>
///   <item><description>
///     <see cref="DockedSearchBar(string, Action{string}, Action{string}, bool, Action{bool})"/>
///     — the even older query-based variant (M3 1.0 form: the input
///     field is built into the composable; caller supplies
///     <see cref="LeadingIcon"/>, <see cref="TrailingIcon"/>, and
///     <see cref="Placeholder"/> slots).
///   </description></item>
/// </list>
/// </summary>
/// <remarks>
/// The Material 3 1.4 design replaces both variants with the state-based
/// <see cref="SearchBar"/> + <see cref="ExpandedDockedSearchBar"/> pair
/// (sharing a <see cref="SearchBarState"/>); use that pair for new code.
/// Both overloads here remain because <c>compose-samples</c> ports (e.g.
/// the Reply sample) still bind against the deprecated APIs.
///
/// Collection-initialized children render as the docked-popup result list
/// inside the bar's <c>content</c> lambda for both overloads (the
/// underlying Kotlin lambda receives a <c>ColumnScope</c>).
/// </remarks>
[Obsolete("Use the state-based SearchBar + ExpandedDockedSearchBar pair instead.")]
public sealed class DockedSearchBar : ComposableContainer
{
    readonly bool _isQueryBased;

    // Boolean-state (M3 1.2+) ctor.
    readonly bool _expanded;
    readonly Action<bool>? _onExpandedChange;

    // Query-based (M3 1.0) ctor.
    readonly string? _query;
    readonly Action<string>? _onQueryChange;
    readonly Action<string>? _onSearch;
    readonly bool _active;
    readonly Action<bool>? _onActiveChange;

    /// <summary>
    /// Constructs the boolean-state variant. The caller toggles
    /// <paramref name="expanded"/> in response to
    /// <paramref name="onExpandedChange"/> and supplies an
    /// <see cref="InputField"/> slot.
    /// </summary>
    public DockedSearchBar(bool expanded, Action<bool> onExpandedChange)
    {
        _expanded = expanded;
        _onExpandedChange = onExpandedChange;
    }

    /// <summary>
    /// Constructs the query-based variant. The bar renders its own input
    /// field driven by <paramref name="query"/> /
    /// <paramref name="onQueryChange"/>; tapping it toggles
    /// <paramref name="active"/> via <paramref name="onActiveChange"/>,
    /// and pressing IME-Search fires <paramref name="onSearch"/>.
    /// Decorate the field with <see cref="LeadingIcon"/>,
    /// <see cref="TrailingIcon"/>, and <see cref="Placeholder"/>.
    /// </summary>
    public DockedSearchBar(
        string                query,
        Action<string> onQueryChange,
        Action<string> onSearch,
        bool                  active,
        Action<bool> onActiveChange)
    {
        _isQueryBased = true;
        _query = query;
        _onQueryChange = onQueryChange;
        _onSearch = onSearch;
        _active = active;
        _onActiveChange = onActiveChange;
    }

    /// <summary>
    /// Boolean-state variant: required composable that renders the
    /// search input field at the top of the bar.
    /// </summary>
    public ComposableNode? InputField { get; set; }

    /// <summary>
    /// Query-based variant: optional leading-icon slot (e.g. a search
    /// glyph) shown at the start of the built-in input field.
    /// </summary>
    public ComposableNode? LeadingIcon { get; set; }

    /// <summary>
    /// Query-based variant: optional trailing-icon slot (e.g. a clear
    /// or close button) shown at the end of the built-in input field.
    /// </summary>
    public ComposableNode? TrailingIcon { get; set; }

    /// <summary>
    /// Query-based variant: optional placeholder shown inside the
    /// built-in input field while <c>query</c> is empty.
    /// </summary>
    public ComposableNode? Placeholder { get; set; }

    /// <inheritdoc />
    public override void Render(IComposer composer)
    {
        if (_isQueryBased)
            RenderQueryBased(composer);
        else
            RenderBooleanState(composer);
    }

    void RenderBooleanState(IComposer composer)
    {
        if (InputField is null)
            throw new InvalidOperationException(
                "DockedSearchBar.InputField is required (the Kotlin parameter has no default).");

        var inputField       = ComposableLambdas.Wrap2(composer, c => InputField.Render(c));
        var onExpandedChange = new ComposableLambda1(o =>
            _onExpandedChange!(((Java.Lang.Boolean)o!).BooleanValue()));
        var content          = ComposableLambdas.Wrap3(composer, c => RenderChildren(c));

        ComposeBridges.DockedSearchBar(
            inputField:       inputField,
            expanded:         _expanded,
            onExpandedChange: onExpandedChange,
            modifier:         BuildModifier(),
            content:          content,
            composer:         composer);
    }

    void RenderQueryBased(IComposer composer)
    {
        var onQueryChange  = new ComposableLambda1(o =>
            _onQueryChange!(o?.ToString() ?? string.Empty));
        var onSearch       = new ComposableLambda1(o =>
            _onSearch!(o?.ToString() ?? string.Empty));
        var onActiveChange = new ComposableLambda1(o =>
            _onActiveChange!(((Java.Lang.Boolean)o!).BooleanValue()));

        var placeholder  = Placeholder  is null ? null : ComposableLambdas.Wrap2(composer, c => Placeholder.Render(c));
        var leadingIcon  = LeadingIcon  is null ? null : ComposableLambdas.Wrap2(composer, c => LeadingIcon.Render(c));
        var trailingIcon = TrailingIcon is null ? null : ComposableLambdas.Wrap2(composer, c => TrailingIcon.Render(c));
        var content      = ComposableLambdas.Wrap3(composer, c => RenderChildren(c));

        ComposeBridges.DockedSearchBarWithQuery(
            query:          _query!,
            onQueryChange:  onQueryChange,
            onSearch:       onSearch,
            active:         _active,
            onActiveChange: onActiveChange,
            modifier:       BuildModifier(),
            placeholder:    placeholder,
            leadingIcon:    leadingIcon,
            trailingIcon:   trailingIcon,
            content:        content,
            composer:       composer);
    }
}
