using AndroidX.Compose.Animation;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// <c>AnimatedContent</c> — animates between content keyed by
/// <typeparamref name="T"/>. When <see cref="TargetState"/> changes,
/// the previous content animates out as the new content animates in,
/// using Compose's default <c>ContentTransform</c> (a fade + size
/// animation).
/// </summary>
/// <typeparam name="T">
/// The state type. Same constraints as <see cref="Crossfade{T}"/>:
/// a primitive boxable to a <see cref="Java.Lang.Object"/> wrapper,
/// or a <see cref="Java.Lang.Object"/> subclass.
/// </typeparam>
/// <remarks>
/// <para>Mirror of the bound <c>(targetState: T, ...)</c> overload of
/// <c>androidx.compose.animation.AnimatedContentKt.AnimatedContent</c> —
/// no JNI bridge needed; every parameter except <c>targetState</c>
/// and <c>content</c> defaults to Kotlin's value.</para>
/// <para>The <c>content</c> callback receives the state
/// value Compose is currently rendering — during a transition both
/// the previous and new values are rendered simultaneously while one
/// animates out and the other animates in. The
/// <c>AnimatedContentScope</c> receiver (which exposes
/// <c>Modifier.animateEnterExit</c>) is not surfaced in v1.</para>
/// </remarks>
/// <example>
/// <code>
/// var step = Remember(() => new MutableNumberState&lt;int&gt;(0));
/// new AnimatedContent&lt;int&gt;(
///     targetState: step.Value,
///     content:     i => new Text($"Step {i}"))
/// </code>
/// </example>
public sealed class AnimatedContent<T> : ComposableNode
{
    readonly T _targetState;
    readonly Func<T, ComposableNode> _content;

    /// <summary>Build an AnimatedContent keyed on <paramref name="targetState"/> with per-state content.</summary>
    /// <param name="targetState">The state value that drives the transition.</param>
    /// <param name="content">Builds the <see cref="ComposableNode"/> to display for a given state value. Invoked once per active state during the transition.</param>
    public AnimatedContent(T targetState, Func<T, ComposableNode> content)
    {
        _targetState = targetState;
        ArgumentNullException.ThrowIfNull(content);
        _content     = content;
    }

    /// <summary>The state value passed to the underlying <c>AnimatedContent</c>.</summary>
    public T TargetState => _targetState;

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();
        var boxed    = MutableState<T>.ToJava(_targetState);

        // The Function4<AnimatedContentScope, T, Composer, Int, Unit>
        // content slot delivers (scope, boxedState, composer, $changed).
        // We discard the scope (animateEnterExit not surfaced in v1)
        // and unbox the second arg back to T.
        var content = ComposableLambdas.Wrap4(composer, (scope, p1, c) =>
        {
            T value = MutableState<T>.FromJava(p1);
            _content(value).Render(c);
        });

        // $default bit positions (Kotlin source order):
        //   0 = targetState       (always provided → cleared)
        //   1 = modifier          (cleared when caller set it)
        //   2 = transitionSpec    (left set → use Compose default)
        //   3 = contentAlignment  (left set → use Compose default)
        //   4 = label             (left set → use Compose default)
        //   5 = contentKey        (left set → use Compose default)
        //   6 = content           (always provided → cleared)
        int defaults = (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5);
        if (modifier is null) defaults |= 1 << 1;

        AnimatedContentKt.AnimatedContent(
            targetState:      boxed,
            modifier:         modifier,
            transitionSpec:   null,
            contentAlignment: null,
            label:            null,
            contentKey:       null,
            content:          content,
            _composer:        composer,
            p8:               0,            // $changed
            _changed:         defaults);    // $default
    }
}
