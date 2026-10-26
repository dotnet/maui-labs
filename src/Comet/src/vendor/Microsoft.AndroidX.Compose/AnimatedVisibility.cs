using AndroidX.Compose.Animation;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// <c>AnimatedVisibility</c> — animates the appearance and disappearance
/// of its content as <see cref="Visible"/> toggles. By default Compose
/// fades and expands/shrinks the content; pass <c>enter</c>
/// and <c>exit</c> to the constructor to choose different
/// transitions (build them with the <see cref="Transitions"/> factories
/// — <c>Transitions.FadeIn()</c>, <c>Transitions.SlideInVertically()</c>,
/// etc., or combine them with the <c>+</c> operator from the bound
/// Compose API).
/// </summary>
/// <remarks>
/// Children are rendered through Compose's <c>AnimatedVisibilityScope</c>
/// receiver. The scope's helpers (<c>Modifier.animateEnterExit</c>) are
/// not exposed in v1 — children render with the discarded scope.
/// Mirror of the bound
/// <see cref="AnimatedVisibilityKt.AnimatedVisibility(bool, AndroidX.Compose.UI.IModifier?, EnterTransition?, ExitTransition?, string?, Kotlin.Jvm.Functions.IFunction3, IComposer?, int, int)"/>
/// overload — no JNI bridge. Enter and exit must be ctor parameters
/// (not init-only properties) because <see cref="AnimatedVisibility"/>
/// is a <see cref="ComposableContainer"/> — C# disallows mixing
/// collection initializers with object initializers.
/// </remarks>
public sealed class AnimatedVisibility : ComposableContainer
{
    readonly bool _visible;
    readonly EnterTransition? _enter;
    readonly ExitTransition? _exit;

    /// <summary>Build an AnimatedVisibility container with the given visibility and optional transitions.</summary>
    /// <param name="visible">When <see langword="true"/> the content is shown (entering if previously hidden); when <see langword="false"/> the content is hidden (exiting if previously shown).</param>
    /// <param name="enter">Optional enter transition (fade-in, slide-in, scale-in, …) applied when <paramref name="visible"/> flips to <see langword="true"/>. Build via the <see cref="Transitions"/> factories. When <see langword="null"/> (default), Compose uses its own fade-plus-expand default.</param>
    /// <param name="exit">Optional exit transition applied when <paramref name="visible"/> flips to <see langword="false"/>. Build via the <see cref="Transitions"/> factories. When <see langword="null"/> (default), Compose uses its own fade-plus-shrink default.</param>
    public AnimatedVisibility(bool visible, EnterTransition? enter = null, ExitTransition? exit = null)
    {
        _visible = visible;
        _enter = enter;
        _exit = exit;
    }

    /// <summary>Current visibility passed to the underlying Compose <c>AnimatedVisibility</c>.</summary>
    public bool Visible => _visible;

    /// <summary>The enter transition supplied at construction, or <see langword="null"/> for the Compose default.</summary>
    public EnterTransition? Enter => _enter;

    /// <summary>The exit transition supplied at construction, or <see langword="null"/> for the Compose default.</summary>
    public ExitTransition? Exit => _exit;

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();
        var content  = ComposableLambdas.Wrap3(composer, RenderChildren);

        // We never pass `label`, so it always stays set.
        var defaults = AnimatedVisibilityDefault.Label;
        if (modifier is null) defaults |= AnimatedVisibilityDefault.Modifier;
        if (_enter is null)   defaults |= AnimatedVisibilityDefault.Enter;
        if (_exit is null)    defaults |= AnimatedVisibilityDefault.Exit;

        AnimatedVisibilityKt.AnimatedVisibility(
            visible:   _visible,
            modifier:  modifier,
            enter:     _enter,
            exit:      _exit,
            label:     null,
            content:   content,
            _composer: composer,
            p7:        0,                // $changed
            _changed:  (int)defaults);   // $default
    }
}
