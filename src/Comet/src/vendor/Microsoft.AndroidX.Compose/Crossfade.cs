using AndroidX.Compose.Animation;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// <c>Crossfade</c> — fades between content keyed by
/// <typeparamref name="T"/>. When <see cref="TargetState"/> changes,
/// the content for the previous and new states are both rendered for
/// the duration of the crossfade animation while one fades out and
/// the other fades in.
/// </summary>
/// <typeparam name="T">
/// The state type. Must be one of the <see cref="MutableState{T}"/>-supported
/// boxable types: a primitive (<c>bool</c>, <c>int</c>, <c>long</c>,
/// <c>float</c>, <c>double</c>, <c>string</c>, <c>char</c>, the byte
/// / short / unsigned numeric variants), or a
/// <see cref="Java.Lang.Object"/> subclass. Other reference types are
/// not supported because there is no clean Java box for them — wrap
/// such state in a key type the user controls (e.g. an
/// <c>int</c> page index) and render the actual content in the
/// callback.
/// </typeparam>
/// <remarks>
/// <para>Mirror of the bound <c>(targetState: T, ...)</c> overload of
/// <c>androidx.compose.animation.CrossfadeKt.Crossfade</c> — no JNI
/// bridge needed; every parameter except <c>targetState</c> and
/// <c>content</c> defaults to Kotlin's value.</para>
/// <para>The <c>content</c> callback receives the state
/// value Compose is currently rendering — typically the
/// <see cref="TargetState"/> after settling, but during a crossfade
/// transition it is invoked once per active state (the previous and
/// the new value). Make sure the callback returns the right node for
/// the value it receives, not just for the current
/// <see cref="TargetState"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var page = Remember(() => new MutableNumberState&lt;int&gt;(0));
/// new Crossfade&lt;int&gt;(
///     targetState: page.Value,
///     content:     i => new Text($"Page {i}"))
/// </code>
/// </example>
public sealed class Crossfade<T> : ComposableNode
{
    readonly T _targetState;
    readonly Func<T, ComposableNode> _content;

    /// <summary>Build a Crossfade keyed on <paramref name="targetState"/> with per-state content.</summary>
    /// <param name="targetState">The state value that drives the fade.</param>
    /// <param name="content">Builds the <see cref="ComposableNode"/> to display for a given state value. Invoked once per active state during a crossfade transition.</param>
    public Crossfade(T targetState, Func<T, ComposableNode> content)
    {
        _targetState = targetState;
        ArgumentNullException.ThrowIfNull(content);
        _content     = content;
    }

    /// <summary>The state value passed to the underlying <c>Crossfade</c>.</summary>
    public T TargetState => _targetState;

    public override void Render(IComposer composer)
    {
        var modifier  = BuildModifier();
        var boxed     = MutableState<T>.ToJava(_targetState);

        // p0 carries the boxed state value (Function3<T, Composer, Int, Unit>).
        // Unbox back to T so the caller's builder receives a typed value
        // matching whatever Compose is rendering for this pass — could be
        // the previous OR new target during a crossfade transition.
        var content = ComposableLambdas.Wrap3WithValue(composer, (p0, c) =>
        {
            T value = MutableState<T>.FromJava(p0);
            _content(value).Render(c);
        });

        // $default bit positions (Kotlin source order):
        //   0 = targetState    (always provided → cleared)
        //   1 = modifier       (cleared when caller set it)
        //   2 = animationSpec  (left set → use Compose default tween)
        //   3 = label          (left set → use Compose default)
        //   4 = content        (always provided → cleared)
        int defaults = (1 << 2) | (1 << 3);
        if (modifier is null) defaults |= 1 << 1;

        CrossfadeKt.Crossfade(
            targetState:   boxed,
            modifier:      modifier,
            animationSpec: null,
            label:         null,
            content:       content,
            _composer:     composer,
            p6:            0,           // $changed
            _changed:      defaults);   // $default
    }
}
