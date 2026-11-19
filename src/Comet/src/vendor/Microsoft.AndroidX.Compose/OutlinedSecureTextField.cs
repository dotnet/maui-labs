using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>OutlinedSecureTextField</c> — the outlined variant of
/// <see cref="SecureTextField"/> for password / secure input. Stripped
/// from the binding for the same reason (<c>@JvmInline value class</c>
/// parameter, hashed JVM name <c>-XvU6IwQ</c>); reached via
/// <see cref="ComposeBridges"/>.
/// </summary>
/// <remarks>
/// Same shape as <see cref="SecureTextField"/> — see that type's docs for
/// the full list of optional slots and which Kotlin defaults are used for
/// the parameters this facade does not expose.
///
/// <code>
/// var pwd = Remember(() =&gt; new SecureTextFieldState());
/// new OutlinedSecureTextField(pwd)
/// {
///     Label       = new Text("Password"),
///     LeadingIcon = new Text("🔒"),
/// }
/// </code>
/// </remarks>
public sealed class OutlinedSecureTextField : ComposableNode
{
    readonly SecureTextFieldState _state;

    /// <summary>Creates an outlined secure text field bound to the supplied <see cref="SecureTextFieldState"/>.</summary>
    public OutlinedSecureTextField(SecureTextFieldState state) => _state = state;

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

    /// <summary>Optional shape applied to the field's outlined container (Kotlin <c>shape</c>).</summary>
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

        ComposeBridges.OutlinedSecureTextField(
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
