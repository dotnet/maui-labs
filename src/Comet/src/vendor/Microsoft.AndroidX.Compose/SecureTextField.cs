using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>SecureTextField</c> — a single-line filled text field
/// designed for password / secure input. Obscures typed characters with
/// a bullet glyph, disables cut/copy/drag, and pre-configures the IME
/// for password input. Stripped from the binding because
/// <c>textObfuscationMode</c> is a <c>@JvmInline value class</c> (hashed
/// JVM name <c>-XvU6IwQ</c>); reached via <see cref="ComposeBridges"/>.
/// </summary>
/// <remarks>
/// State-based: pass a <see cref="SecureTextFieldState"/> the same way
/// <see cref="SearchBarInputField"/> takes a
/// <see cref="SearchBarTextFieldState"/>. Read <c>state.Text</c> to drive
/// other UI off the typed value (e.g. a "Sign in" button's enabled flag).
///
/// <para>The Kotlin signature has 23 parameters; this facade exposes the
/// most commonly toggled ones (<c>enabled</c>, <c>isError</c>) plus the
/// optional content slots (<c>label</c>, <c>placeholder</c>,
/// <c>leadingIcon</c>, <c>trailingIcon</c>, <c>supportingText</c>). All
/// other Kotlin parameters — <c>textStyle</c>, <c>labelPosition</c>,
/// <c>prefix</c>, <c>suffix</c>, <c>inputTransformation</c>,
/// <c>textObfuscationMode</c>, <c>textObfuscationCharacter</c>,
/// <c>keyboardOptions</c>, <c>onKeyboardAction</c>, <c>onTextLayout</c>,
/// <c>colors</c>, <c>contentPadding</c>,
/// <c>interactionSource</c> — fall back to their Compose defaults
/// (which configure a <c>RevealLastTyped</c> obfuscation mode and a
/// password-typed IME).</para>
///
/// <code>
/// var pwd = Remember(() =&gt; new SecureTextFieldState());
/// new SecureTextField(pwd)
/// {
///     Label       = new Text("Password"),
///     LeadingIcon = new Text("🔒"),
/// }
/// </code>
/// </remarks>
public sealed class SecureTextField : ComposableNode
{
    readonly SecureTextFieldState _state;

    /// <summary>Creates a secure text field bound to the supplied <see cref="SecureTextFieldState"/>.</summary>
    public SecureTextField(SecureTextFieldState state) => _state = state;

    /// <summary>Whether the field accepts user input. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether to render the field in its error appearance. Defaults to <c>false</c>.</summary>
    public bool IsError { get; set; }

    /// <summary>Optional label slot (Kotlin <c>label</c>).</summary>
    public ComposableNode? Label { get; set; }

    /// <summary>Optional placeholder slot drawn when the field is empty (Kotlin <c>placeholder</c>).</summary>
    public ComposableNode? Placeholder { get; set; }

    /// <summary>Optional leading icon slot (Kotlin <c>leadingIcon</c>).</summary>
    public ComposableNode? LeadingIcon { get; set; }

    /// <summary>Optional trailing icon slot, typically a "show/hide" toggle (Kotlin <c>trailingIcon</c>).</summary>
    public ComposableNode? TrailingIcon { get; set; }

    /// <summary>Optional supporting text rendered below the field (Kotlin <c>supportingText</c>).</summary>
    public ComposableNode? SupportingText { get; set; }

    /// <summary>Optional shape applied to the field's container (Kotlin <c>shape</c>).</summary>
    public Shape? Shape { get; set; }

    public override void Render(IComposer composer)
    {
        var statePeer = _state.Resolve(composer);

        var labelNode = Label;
        var label = labelNode is null
            ? null
            : ComposableLambdas.Wrap3(composer, c => labelNode.Render(c));

        var placeholderNode = Placeholder;
        var placeholder = placeholderNode is null
            ? null
            : ComposableLambdas.Wrap2(composer, c => placeholderNode.Render(c));

        var leadingIconNode = LeadingIcon;
        var leadingIcon = leadingIconNode is null
            ? null
            : ComposableLambdas.Wrap2(composer, c => leadingIconNode.Render(c));

        var trailingIconNode = TrailingIcon;
        var trailingIcon = trailingIconNode is null
            ? null
            : ComposableLambdas.Wrap2(composer, c => trailingIconNode.Render(c));

        var supportingTextNode = SupportingText;
        var supportingText = supportingTextNode is null
            ? null
            : ComposableLambdas.Wrap2(composer, c => supportingTextNode.Render(c));

        ComposeBridges.SecureTextField(
            state:          statePeer.Handle,
            modifier:       BuildModifier(),
            enabled:        Enabled,
            label:          label,
            placeholder:    placeholder,
            leadingIcon:    leadingIcon,
            trailingIcon:   trailingIcon,
            supportingText: supportingText,
            isError:        IsError,
            shape:          Shape,
            composer:       composer);
    }
}
