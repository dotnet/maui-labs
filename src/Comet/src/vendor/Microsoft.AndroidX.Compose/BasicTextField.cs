using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>BasicTextField</c> (String overload) — the un-decorated text field: just the
/// editable text and cursor, with no Material container, indicator line, label, or placeholder.
/// Use it (plus a <see cref="Modifier"/> for background/padding) for a borderless input that blends
/// into its surroundings, e.g. a chat composer. Placeholder/affordances are the caller's to draw.
/// </summary>
/// <remarks>
/// Hand-written rather than <c>[ComposeFacade]</c>-generated to keep the surface tiny: only
/// <c>value</c>/<c>onValueChange</c>/<c>modifier</c>/<c>textStyle</c>/<c>singleLine</c> are exposed;
/// every other Kotlin parameter (cursorBrush, decorationBox, keyboard options, …) keeps its default.
/// </remarks>
public sealed class BasicTextField : ComposableNode
{
    readonly string _value;
    readonly System.Action<string> _onValueChange;

    /// <summary>Optional <c>TextStyle</c> (Kotlin <c>textStyle</c>) — text color, size, weight, etc.</summary>
    public TextStyle? TextStyle { get; set; }

    /// <summary>Whether the field is constrained to a single line.</summary>
    public bool? SingleLine { get; set; }

    /// <summary>String-overload ctor — pass the current value and an edit callback.</summary>
    public BasicTextField(string value, System.Action<string> onValueChange)
    {
        _value = value;
        _onValueChange = onValueChange;
    }

    /// <summary>Bind to a <see cref="MutableState{T}"/> of <see cref="string"/> so edits recompose.</summary>
    public BasicTextField(MutableState<string> state)
        : this(state.Value ?? string.Empty, v => state.Value = v)
    {
    }

    /// <inheritdoc/>
    public override void Render(IComposer composer)
    {
        var onValueChange = new ComposableLambda1(v => _onValueChange(v?.ToString() ?? string.Empty));
        ComposeBridges.BasicTextField(_value, onValueChange, BuildModifier(), TextStyle?.Build(), SingleLine, composer);
    }
}
