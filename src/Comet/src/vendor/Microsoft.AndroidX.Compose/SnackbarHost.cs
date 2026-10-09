using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>SnackbarHost</c>. Anchors a <see cref="Snackbar"/>
/// driven by a <see cref="SnackbarHostState"/>. Typically
/// dropped into <see cref="Scaffold.SnackbarHost"/>:
/// <code>
/// var hostState = Remember(() =&gt; new SnackbarHostState());
///
/// new Scaffold
/// {
///     SnackbarHost = new SnackbarHost(hostState),
///     Body         = ...,
/// }
/// </code>
/// </summary>
/// <remarks>
/// Triggering snackbars via <see cref="SnackbarHostState"/>
/// requires Kotlin coroutines and isn't yet wired up in this binding.
/// For most cases, render a <see cref="Snackbar"/> directly into the
/// <see cref="Scaffold.SnackbarHost"/> slot, gated by a
/// <see cref="MutableState{T}"/>. When external code (Kotlin/Java) drives
/// the host state, this host paints whatever the state has queued via
/// the M3 <c>Snackbar(SnackbarData)</c> overload.
/// </remarks>
public sealed class SnackbarHost : ComposableNode
{
    readonly SnackbarHostState _hostState;

    public SnackbarHost(SnackbarHostState hostState) => _hostState = hostState;

    public override void Render(IComposer composer)
    {
        // SnackbarHost's Function3 receives the current SnackbarData as p0.
        // Forward it to the M3 default — Snackbar(snackbarData) — so an
        // externally-driven host state actually paints. The lambda is a
        // no-op when p0 is null (no queued data).
        var snackbar = ComposableLambdas.Wrap3(composer, (data, c) =>
        {
            if (data == IntPtr.Zero) return;
            ComposeBridges.SnackbarFromData(data, modifier: null, composer: c);
        });

        ComposeBridges.SnackbarHost(
            hostState: ((Java.Lang.Object)_hostState.Jvm).Handle,
            modifier:  BuildModifier(),
            snackbar:  snackbar,
            composer:  composer);
    }
}
