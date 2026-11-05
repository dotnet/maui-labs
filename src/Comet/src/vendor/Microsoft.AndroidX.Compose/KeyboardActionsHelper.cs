using AndroidXKeyboardActions = AndroidX.Compose.Foundation.Text.KeyboardActions;

namespace AndroidX.Compose;

/// <summary>
/// C#-friendly factory for Compose's
/// <see cref="AndroidX.Compose.Foundation.Text.KeyboardActions"/> —
/// the per-IME-action callback bag passed to
/// <see cref="OutlinedTextField"/> /
/// <see cref="TextField"/>'s <c>keyboardActions</c> slot.
/// </summary>
/// <remarks>
/// <para>The bound
/// <see cref="AndroidX.Compose.Foundation.Text.KeyboardActions"/>
/// ctor takes <c>IFunction1?</c> parameters whose only consumer is
/// the internal <c>ComposableLambda1</c> JCW. <c>ComposableLambda1</c>
/// is intentionally <c>internal</c> (it's a generated <c>net.compose.*</c>
/// JCW; making it public would freeze the JCW class name into the
/// public API). This helper wraps each non-null
/// <see cref="System.Action"/> in that internal JCW and forwards to
/// the bound ctor — call sites elsewhere stay free of JNI plumbing.</para>
///
/// <para>Each callback receives no payload because Kotlin's
/// <c>KeyboardActionScope</c> only exposes a
/// <c>defaultKeyboardAction()</c> trampoline back to Compose's
/// platform-default behaviour, which we never want to invoke from
/// managed code (the action key is exactly what we're overriding).</para>
/// </remarks>
public static class KeyboardActionsHelper
{
    /// <summary>
    /// Build a <see cref="AndroidXKeyboardActions"/> wrapping the
    /// supplied callbacks. Any <c>null</c> slot keeps Compose's
    /// default (which on the soft-keyboard side dismisses the IME for
    /// every action except <c>None</c>/<c>Default</c>).
    /// </summary>
    public static AndroidXKeyboardActions Create(
        Action? onDone     = null,
        Action? onGo       = null,
        Action? onNext     = null,
        Action? onPrevious = null,
        Action? onSearch   = null,
        Action? onSend     = null) =>
        new AndroidXKeyboardActions(
            onDone:     onDone     is null ? null : new ComposableLambda1(_ => onDone()),
            onGo:       onGo       is null ? null : new ComposableLambda1(_ => onGo()),
            onNext:     onNext     is null ? null : new ComposableLambda1(_ => onNext()),
            onPrevious: onPrevious is null ? null : new ComposableLambda1(_ => onPrevious()),
            onSearch:   onSearch   is null ? null : new ComposableLambda1(_ => onSearch()),
            onSend:     onSend     is null ? null : new ComposableLambda1(_ => onSend()));
}
