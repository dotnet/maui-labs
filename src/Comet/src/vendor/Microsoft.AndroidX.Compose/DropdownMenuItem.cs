using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>DropdownMenuItem</c> — a single selectable row inside a
/// <see cref="DropdownMenu"/>. The <c>text</c> composable and
/// <c>onClick</c> callback are required (passed as constructor arguments).
/// </summary>
/// <remarks>
/// <code>
/// new DropdownMenu(expanded: open.Value, onDismissRequest: () =&gt; open.Value = false)
/// {
///     new DropdownMenuItem(text: new Text("Refresh"), onClick: () =&gt; { open.Value = false; ... }),
///     new DropdownMenuItem(text: new Text("Settings"), onClick: () =&gt; { open.Value = false; ... }),
/// }
/// </code>
/// </remarks>
public sealed class DropdownMenuItem : ComposableNode
{
    readonly ComposableNode _text;
    readonly Action _onClick;

    public DropdownMenuItem(ComposableNode text, Action onClick)
    {
        _text    = text;
        _onClick = onClick;
    }

    /// <summary>Optional: leading icon (composable, typically <see cref="Icon"/>).</summary>
    public ComposableNode? LeadingIcon { get; set; }

    /// <summary>Optional: trailing icon (composable, typically <see cref="Icon"/>).</summary>
    public ComposableNode? TrailingIcon { get; set; }

    /// <summary>Whether the item responds to clicks. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    public override void Render(IComposer composer)
    {
        var text    = ComposableLambdas.Wrap2(composer, c => _text.Render(c));
        var onClick = new ComposableLambda0(_onClick);
        var modifier = BuildModifier();

        Kotlin.Jvm.Functions.IFunction2? leading = LeadingIcon is null ? null
            : ComposableLambdas.Wrap2(composer, c => LeadingIcon.Render(c));
        Kotlin.Jvm.Functions.IFunction2? trailing = TrailingIcon is null ? null
            : ComposableLambdas.Wrap2(composer, c => TrailingIcon.Render(c));

        int defaults = (int)DropdownMenuItemDefault.All;
        if (modifier is not null) defaults &= ~(int)DropdownMenuItemDefault.Modifier;
        if (leading  is not null) defaults &= ~(int)DropdownMenuItemDefault.LeadingIcon;
        if (trailing is not null) defaults &= ~(int)DropdownMenuItemDefault.TrailingIcon;
        if (!Enabled)             defaults &= ~(int)DropdownMenuItemDefault.Enabled;

        AndroidMenu_androidKt.DropdownMenuItem(
            text:                 text,
            onClick:              onClick,
            modifier:             modifier,
            leadingIcon:          leading,
            trailingIcon:         trailing,
            enabled:              Enabled,
            colors:               null,
            contentPadding:       null,
            interactionSource:    null,
            _composer:            composer,
            p10:                  0,
            _changed:             defaults);
    }
}
