using Android.Runtime;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Text.Input;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TextField</c> (filled variant). Three construction
/// modes:
/// <list type="bullet">
/// <item>Plain <c>value</c> + <c>onValueChange</c> pair — string-overload
/// of Kotlin's <c>TextField</c>.</item>
/// <item><see cref="MutableState{T}"/> of <see cref="string"/> — convenience
/// that wires a <see cref="MutableState{T}"/> directly so user edits
/// trigger recomposition.</item>
/// <item><see cref="MutableState{T}"/> of <see cref="TextFieldValue"/> —
/// uses Kotlin's <c>TextField(TextFieldValue, …)</c> overload so callers
/// can place the caret explicitly after programmatic edits (e.g. append
/// an emoji and move the cursor to the end of the buffer). Build values
/// via <see cref="ComposeExtensions.NewTextFieldValue(string, long, AndroidX.Compose.UI.Text.TextRange?)"/>
/// and <c>TextFieldValue.Copy(...)</c>. See issue #204.</item>
/// </list>
/// </summary>
/// <remarks>
/// Hand-written rather than <c>[ComposeFacade]</c>-generated because the
/// facade routes between two distinct <c>[ComposeBridge]</c> methods
/// (<c>TextField</c> string overload vs <c>TextFieldWithValue</c>) based
/// on which ctor was used. The current generator's <c>BranchOn</c> mode
/// only models slot-presence branching, not ctor-driven bridge
/// selection. <see cref="Icon"/> is the existing precedent for the same
/// pattern.
/// </remarks>
public sealed class TextField : ComposableNode
{
    readonly string?                       _value;
    readonly Action<string>?               _onValueChange;
    readonly MutableState<TextFieldValue>? _tfvState;

    /// <summary>Floating label (e.g. <c>new Text("Email")</c>).</summary>
    public ComposableNode? Label          { get; set; }
    /// <summary>Hint shown while the field is empty.</summary>
    public ComposableNode? Placeholder    { get; set; }
    /// <summary>Icon rendered before the value text.</summary>
    public ComposableNode? LeadingIcon    { get; set; }
    /// <summary>Icon rendered after the value text.</summary>
    public ComposableNode? TrailingIcon   { get; set; }
    /// <summary>Static prefix rendered before the value text.</summary>
    public ComposableNode? Prefix         { get; set; }
    /// <summary>Static suffix rendered after the value text.</summary>
    public ComposableNode? Suffix         { get; set; }
    /// <summary>Helper text rendered below the field.</summary>
    public ComposableNode? SupportingText { get; set; }
    /// <summary>Whether the field accepts focus and input.</summary>
    public bool?           Enabled        { get; set; }
    /// <summary>Whether the field is read-only.</summary>
    public bool?           ReadOnly       { get; set; }
    /// <summary>Whether the field is in an error state.</summary>
    public bool?           IsError        { get; set; }
    /// <summary>Whether the field is constrained to one line.</summary>
    public bool?           SingleLine     { get; set; }
    /// <summary>Maximum number of visible lines.</summary>
    public int?            MaxLines       { get; set; }
    /// <summary>Minimum number of visible lines.</summary>
    public int?            MinLines       { get; set; }
    /// <summary>Optional shape applied to the field's container (Kotlin <c>shape</c>).</summary>
    public Shape?          Shape          { get; set; }
    /// <summary>Optional <c>TextStyle</c> override (Kotlin <c>textStyle</c>) — controls text color, size, weight, etc.</summary>
    public TextStyle? TextStyle { get; set; }
    /// <summary>Optional visual transformation (Kotlin <c>visualTransformation</c>) — use <c>PasswordVisualTransformation</c> for password fields.</summary>
    public AndroidX.Compose.UI.Text.Input.IVisualTransformation? VisualTransformation { get; set; }
    /// <summary>Optional keyboard options (Kotlin <c>keyboardOptions</c>) — controls IME type, capitalization, autocorrect.</summary>
    public AndroidX.Compose.Foundation.Text.KeyboardOptions? KeyboardOptions { get; set; }

    /// <summary>String-overload ctor — pass the current value and a callback.</summary>
    public TextField(string value, Action<string> onValueChange)
    {
        _value = value;
        _onValueChange = onValueChange;
    }

    /// <summary>
    /// Bind this <c>TextField</c> to a <see cref="MutableState{T}"/> of
    /// <see cref="string"/> so user edits trigger recomposition automatically.
    /// </summary>
    public TextField(MutableState<string> state)
        : this(state.Value ?? string.Empty, v => state.Value = v)
    {
    }

    /// <summary>
    /// Bind this <c>TextField</c> to a <see cref="MutableState{T}"/> of
    /// <see cref="TextFieldValue"/>. Uses Kotlin's
    /// <c>TextField(TextFieldValue, (TextFieldValue) -&gt; Unit, …)</c>
    /// overload so caller-supplied selection state is honoured (e.g. for
    /// "place caret at end after inserting" behaviour).
    /// </summary>
    public TextField(MutableState<TextFieldValue> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _tfvState = state;
    }

    /// <inheritdoc/>
    public override void Render(IComposer composer)
    {
        if (_tfvState is not null)
            RenderWithSelection(composer);
        else
            RenderString(composer);
    }

    void RenderString(IComposer composer)
    {
        var __onValueChange = new ComposableLambda1(v => _onValueChange!(v?.ToString() ?? string.Empty));
        var __label          = Label          is null ? null : ComposableLambdas.Wrap2(composer, c => Label.Render(c));
        var __placeholder    = Placeholder    is null ? null : ComposableLambdas.Wrap2(composer, c => Placeholder.Render(c));
        var __leadingIcon    = LeadingIcon    is null ? null : ComposableLambdas.Wrap2(composer, c => LeadingIcon.Render(c));
        var __trailingIcon   = TrailingIcon   is null ? null : ComposableLambdas.Wrap2(composer, c => TrailingIcon.Render(c));
        var __prefix         = Prefix         is null ? null : ComposableLambdas.Wrap2(composer, c => Prefix.Render(c));
        var __suffix         = Suffix         is null ? null : ComposableLambdas.Wrap2(composer, c => Suffix.Render(c));
        var __supportingText = SupportingText is null ? null : ComposableLambdas.Wrap2(composer, c => SupportingText.Render(c));
        ComposeBridges.TextField(_value!, __onValueChange, BuildModifier(),
            Enabled, ReadOnly, TextStyle?.Build(), __label, __placeholder, __leadingIcon, __trailingIcon,
            __prefix, __suffix, __supportingText, IsError,
            VisualTransformation, KeyboardOptions,
            SingleLine, MaxLines, MinLines,
            Shape,
            composer);
    }

    void RenderWithSelection(IComposer composer)
    {
        var state = _tfvState!;
        var current = state.Value
            ?? throw new InvalidOperationException(
                $"{nameof(TextField)}: MutableState<TextFieldValue>.Value is null. " +
                $"Seed with {nameof(Compose)}.{nameof(ComposeExtensions.NewTextFieldValue)}() before first render.");

        var __onValueChange = new ComposableLambda1(v =>
        {
            // Compose hands us the fresh Kotlin TextFieldValue peer; the
            // peer registry maps it back to the bound binding type so we
            // can store it directly.
            state.Value = Java.Lang.Object.GetObject<TextFieldValue>(
                v!.Handle, JniHandleOwnership.DoNotTransfer)!;
        });
        var __label          = Label          is null ? null : ComposableLambdas.Wrap2(composer, c => Label.Render(c));
        var __placeholder    = Placeholder    is null ? null : ComposableLambdas.Wrap2(composer, c => Placeholder.Render(c));
        var __leadingIcon    = LeadingIcon    is null ? null : ComposableLambdas.Wrap2(composer, c => LeadingIcon.Render(c));
        var __trailingIcon   = TrailingIcon   is null ? null : ComposableLambdas.Wrap2(composer, c => TrailingIcon.Render(c));
        var __prefix         = Prefix         is null ? null : ComposableLambdas.Wrap2(composer, c => Prefix.Render(c));
        var __suffix         = Suffix         is null ? null : ComposableLambdas.Wrap2(composer, c => Suffix.Render(c));
        var __supportingText = SupportingText is null ? null : ComposableLambdas.Wrap2(composer, c => SupportingText.Render(c));
        ComposeBridges.TextFieldWithValue(current, __onValueChange, BuildModifier(),
            Enabled, ReadOnly, TextStyle?.Build(), __label, __placeholder, __leadingIcon, __trailingIcon,
            __prefix, __suffix, __supportingText, IsError,
            VisualTransformation, KeyboardOptions,
            SingleLine, MaxLines, MinLines,
            Shape,
            composer);
    }
}
