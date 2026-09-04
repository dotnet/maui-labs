using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ExtendedFloatingActionButton</c> — the animated
/// extended FAB with separate <c>Icon</c> and <c>Text</c> slots and an
/// <c>Expanded</c> flag that animates between the icon-only and
/// icon&#x202F;+&#x202F;text states.
///
/// <code>
/// new ExtendedFloatingActionButton(onClick: () => count.Value++, expanded: true)
/// {
///     Icon = new Text("+"),
///     Text = new Text("Add"),
/// }
/// </code>
///
/// Both <see cref="Icon"/> and <see cref="Text"/> are required — the
/// underlying Kotlin parameters have no default. Setting either to
/// <c>null</c> throws <see cref="InvalidOperationException"/> at
/// render time.
/// </summary>
public sealed partial class ExtendedFloatingActionButton
{
    /// <summary>
    /// Calls the real <c>ExtendedFloatingActionButton</c> Kotlin bridge directly,
    /// bypassing the generated slot-property indirection. Use from the Comet platform
    /// layer where the generated slot-property Render path is unavailable (different
    /// assembly) but the public static call surface is.
    /// The <paramref name="icon"/> and <paramref name="text"/> nodes are wrapped in
    /// <c>ComposableLambda</c>s via <see cref="ComposableLambdas.Wrap2"/> so Compose
    /// tracks their identity across recompositions.
    /// </summary>
    public static void RenderDirect(
        ComposableNode icon,
        ComposableNode text,
        System.Action onClick,
        bool expanded,
        Color? containerColor,
        Color? contentColor,
        Modifier? modifier,
        IComposer composer)
    {
        ComposeBridges.ExtendedFloatingActionButton(
            text:           ComposableLambdas.Wrap2(composer, c => text.Render(c)),
            icon:           ComposableLambdas.Wrap2(composer, c => icon.Render(c)),
            onClick:        new UnitCallback(onClick),
            modifier:       modifier?.Build(),
            expanded:       expanded,
            shape:          null,
            containerColor: containerColor,
            contentColor:   contentColor,
            composer:       composer);
    }
}
