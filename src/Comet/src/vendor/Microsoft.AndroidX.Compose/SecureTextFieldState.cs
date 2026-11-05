using AndroidX.Compose.Foundation.Text.Input;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for the text inside a
/// <see cref="SecureTextField"/> or <see cref="OutlinedSecureTextField"/>.
/// Mirrors <see cref="SearchBarTextFieldState"/>: the underlying JVM
/// <c>androidx.compose.foundation.text.input.TextFieldState</c> is created
/// lazily the first time a <see cref="SecureTextField"/> bound to this
/// state is rendered, via Compose's <c>rememberTextFieldState</c>.
/// </summary>
/// <remarks>
/// Read <see cref="Text"/> inside composition to drive other UI off the
/// typed value (e.g. enable a "Sign in" button when the password is
/// non-empty); reads go through to the live JVM peer's <c>text</c>
/// property, which is itself a Compose snapshot state, so subscribing
/// inside composition triggers recomposition.
///
/// <para>The <c>initialText</c> constructor argument only seeds the
/// JVM peer the first time a <see cref="SecureTextField"/> bound to
/// this state is rendered. Once the peer exists, the seed value is no
/// longer consulted — to clear or reset the text, mutate the live JVM
/// state via the typed UI or persist your own <see cref="MutableState{T}"/>-backed
/// mirror.</para>
///
/// <code>
/// var pwd = Remember(() =&gt; new SecureTextFieldState());
/// new Column
/// {
///     new SecureTextField(pwd) { Label = new Text("Password") },
///     new Button(onClick: () =&gt; SignIn(pwd.Text), enabled: pwd.Text.Length &gt;= 8)
///     {
///         new Text("Sign in"),
///     },
/// }
/// </code>
/// </remarks>
public sealed class SecureTextFieldState
{
    readonly string _initialText;

    // Bound peer for the JVM androidx.compose.foundation.text.input.TextFieldState.
    // Set on first SecureTextField render and reused by every subsequent
    // recomposition of the same node so the JVM-side text + cursor state
    // survives across recompositions.
    internal TextFieldState? Jvm;

    /// <summary>
    /// Creates a state holder with an optional initial text value
    /// (defaults to the empty string). The initial value is only used
    /// the first time a <see cref="SecureTextField"/> bound to this state
    /// is rendered — once the JVM peer is resolved, mutations to the
    /// passed-in string are ignored.
    /// </summary>
    public SecureTextFieldState(string initialText = "") =>
        _initialText = initialText;

    /// <summary>
    /// Current text in the secure input. Reads through to the live
    /// JVM <c>TextFieldState.text</c> (a Compose snapshot value), so
    /// reading inside composition subscribes to recomposition. Returns
    /// the initial value before the first render binds the peer.
    /// </summary>
    public string Text => Jvm?.Text ?? _initialText;

    // Lazy-resolve the bound JVM peer. Subsequent SecureTextField renders
    // sharing this state hit the JNI bridge once on the FIRST render and
    // reuse the peer for every recomposition.
    internal TextFieldState Resolve(IComposer composer)
    {
        if (Jvm is not null)
            return Jvm;

        // Bit 1 = default initialSelection (Kotlin places the cursor at
        // the end of initialText). initialText is provided explicitly.
        Jvm = TextFieldStateKt.RememberTextFieldState(_initialText, 0L, composer, 0, 2);
        return Jvm;
    }
}
