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
    readonly string? _value;
    readonly System.Action<string>? _onValueChange;
    readonly UI.Text.Input.TextFieldValue? _tfv;
    readonly System.Action<UI.Text.Input.TextFieldValue>? _onTfvChange;

    /// <summary>Optional <c>TextStyle</c> (Kotlin <c>textStyle</c>) — text color, size, weight, etc.</summary>
    public TextStyle? TextStyle { get; set; }

    /// <summary>Optional IME config (Kotlin <c>keyboardOptions</c>) — keyboard type + the soft-keyboard
    /// action key (e.g. ImeAction.Send). Null keeps Compose's default.</summary>
    public AndroidX.Compose.Foundation.Text.KeyboardOptions? KeyboardOptions { get; set; }

    /// <summary>Optional per-IME-action callbacks (Kotlin <c>keyboardActions</c>) — e.g. an onSend handler
    /// fired when the keyboard's Send key is pressed. Build via <see cref="KeyboardActionsHelper"/>.</summary>
    public AndroidX.Compose.Foundation.Text.KeyboardActions? KeyboardActions { get; set; }

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

    /// <summary>
    /// TextFieldValue-overload ctor — the callback hands back the full
    /// <see cref="UI.Text.Input.TextFieldValue"/> (text + selection + composition), and
    /// caller-supplied selection is honoured on render (programmatic caret placement,
    /// e.g. insert-at-cursor). Build values via <c>ComposeExtensions.NewTextFieldValue</c>.
    /// </summary>
    public BasicTextField(UI.Text.Input.TextFieldValue value, System.Action<UI.Text.Input.TextFieldValue> onValueChange)
    {
        _tfv = value;
        _onTfvChange = onValueChange;
    }

    /// <inheritdoc/>
    public override void Render(IComposer composer)
    {
        if (_tfv is not null)
        {
            // Compose hands the fresh Kotlin TextFieldValue peer to the callback; the peer
            // registry maps it back to the bound binding type (same pattern as the Material
            // TextFieldWithValue path).
            var onTfvChange = new ComposableLambda1(v => _onTfvChange!(
                Java.Lang.Object.GetObject<UI.Text.Input.TextFieldValue>(
                    v!.Handle, Android.Runtime.JniHandleOwnership.DoNotTransfer)!));
            ComposeBridges.BasicTextFieldWithValue(_tfv, onTfvChange, BuildModifier(), TextStyle?.Build(), KeyboardOptions, KeyboardActions, SingleLine, composer);
            return;
        }

        var onValueChange = new ComposableLambda1(v => _onValueChange!(v?.ToString() ?? string.Empty));
        ComposeBridges.BasicTextField(_value!, onValueChange, BuildModifier(), TextStyle?.Build(), KeyboardOptions, KeyboardActions, SingleLine, composer);
    }
}
