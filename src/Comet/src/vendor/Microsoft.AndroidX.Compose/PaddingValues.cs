using Android.Runtime;
using AndroidX.Compose.Foundation.Layout;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.foundation.layout.PaddingValues</c>.
/// Models inset / asymmetric padding to apply inside scroll containers
/// (<see cref="LazyColumn{T}"/>, <see cref="LazyRow{T}"/>, the lazy
/// grids and pagers) and the <c>Button</c> family's
/// <c>contentPadding</c> slot.
/// </summary>
/// <remarks>
/// <para>
/// Compose's <c>PaddingValues</c> is an interface produced by the
/// <c>PaddingValues(Dp)</c>, <c>PaddingValues(Dp, Dp)</c> and
/// <c>PaddingValues(Dp, Dp, Dp, Dp)</c> top-level Kotlin factories.
/// Those factories are all bound (the binder generates them as
/// <c>PaddingKt.PaddingValues(...)</c>), so the wrapper just builds the
/// peer eagerly in its constructor and hands the resulting JNI handle
/// to the bridge generator's reference-type code path (which writes
/// <c>((Java.Lang.Object)pv).Handle</c> into the JNI slot).
/// </para>
/// <para>
/// Hoist with <c>Remember</c> when constructed inside a
/// <c>Build</c> lambda — every <c>new PaddingValues(...)</c> does a JNI
/// round-trip and a managed peer wrap, and Compose recomposition
/// skipping is reference-equality based, so building a fresh instance
/// per recomposition prevents recomposition-skipping on the consuming
/// composable.
/// </para>
/// <code>
/// new LazyColumn&lt;int&gt;(
///     items:       Enumerable.Range(0, 100).ToList(),
///     itemContent: i =&gt; new Text($"Row {i}"))
/// {
///     ContentPadding = new PaddingValues(start: 16, top: 24, end: 16, bottom: 24),
/// }
/// </code>
/// </remarks>
public sealed class PaddingValues : Java.Lang.Object
{
    // Cache the IPaddingValues interface peer so render-path readers
    // (LazyColumn / LazyRow / ... pass .Jvm into the bound LazyDslKt
    // call) don't pay a JavaCast per recomposition.
    readonly IPaddingValues _jvm;

    PaddingValues(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
        _jvm = this.JavaCast<IPaddingValues>();
    }

    /// <summary>
    /// <c>PaddingValues(all: Dp)</c> — equal padding on all four edges.
    /// </summary>
    public PaddingValues(Dp all)
        : this(BuildHandle(PaddingKt.PaddingValues(all.Value)),
               JniHandleOwnership.TransferLocalRef) { }

    /// <summary>
    /// <c>PaddingValues(horizontal: Dp, vertical: Dp)</c> — equal
    /// padding on the start/end edges (<paramref name="horizontal"/>)
    /// and equal padding on the top/bottom edges
    /// (<paramref name="vertical"/>).
    /// </summary>
    public PaddingValues(Dp horizontal, Dp vertical)
        : this(BuildHandle(PaddingKt.PaddingValues(horizontal.Value, vertical.Value)),
               JniHandleOwnership.TransferLocalRef) { }

    /// <summary>
    /// <c>PaddingValues(start: Dp, top: Dp, end: Dp, bottom: Dp)</c> —
    /// independent padding per edge. Any argument left at its default
    /// value (<c>0.dp</c>) leaves that edge unpadded. Use named
    /// arguments for readability:
    /// <c>new PaddingValues(top: 8, bottom: 16)</c>.
    /// </summary>
    public PaddingValues(Dp start = default, Dp top = default,
                         Dp end = default, Dp bottom = default)
        : this(BuildHandle(PaddingKt.PaddingValues(start.Value, top.Value, end.Value, bottom.Value)),
               JniHandleOwnership.TransferLocalRef) { }

    // The bound PaddingKt.PaddingValues(...) factories return an
    // IPaddingValues whose underlying JNI handle is the freshly-
    // constructed peer. We NewLocalRef + TransferLocalRef into the
    // base class so the wrapper owns its own reference (the temporary
    // IPaddingValues peer will get collected on the next GC pass).
    static IntPtr BuildHandle(IPaddingValues result)
        => JNIEnv.NewLocalRef(((Java.Lang.Object)result).Handle);

    /// <summary>
    /// Wrap a runtime <c>PaddingValues</c> JNI handle handed to us by a
    /// parent layout (e.g. <see cref="Scaffold"/>'s content lambda)
    /// without taking ownership of the reference. The resulting wrapper
    /// is only valid for the synchronous duration of the parent's
    /// content lambda — long enough for descendants to read
    /// <see cref="LazyColumn{T}.ContentPadding"/> during the same
    /// composition pass, but it must not be captured into long-lived
    /// state.
    /// </summary>
    internal static PaddingValues Wrap(IntPtr handle) =>
        new PaddingValues(handle, JniHandleOwnership.DoNotTransfer);

    /// <summary>
    /// The underlying bound <see cref="IPaddingValues"/> peer. Used by
    /// hand-written facades that call the bound binding directly
    /// (e.g. <c>LazyDslKt.LazyColumn(contentPadding: ...)</c>);
    /// generated facades / bridges pass the raw handle instead via
    /// <c>((Java.Lang.Object)pv).Handle</c>.
    /// </summary>
    internal IPaddingValues Jvm => _jvm;

    /// <summary>
    /// <c>calculateTopPadding()</c> — the configured top padding, in
    /// <see cref="Dp"/>. Wraps the bound
    /// <see cref="IPaddingValues.CalculateTopPadding"/> reader.
    /// </summary>
    public Dp Top => new Dp(_jvm.CalculateTopPadding());

    /// <summary>
    /// <c>calculateBottomPadding()</c> — the configured bottom
    /// padding, in <see cref="Dp"/>. Wraps the bound
    /// <see cref="IPaddingValues.CalculateBottomPadding"/> reader.
    /// </summary>
    public Dp Bottom => new Dp(_jvm.CalculateBottomPadding());
}
