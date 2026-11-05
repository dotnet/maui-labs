using System.Runtime.CompilerServices;
using Android.Runtime;
using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Static factories mirroring Kotlin's
/// <c>androidx.compose.material3.TopAppBarDefaults</c> singleton — each
/// returns an <see cref="ITopAppBarScrollBehavior"/> configured for a
/// particular collapse strategy. Surfaced as <see cref="IComposer"/>
/// extensions so call sites read
/// <c>composer.PinnedScrollBehavior(state)</c>.
/// </summary>
public static partial class ComposeExtensions
{
    /// <summary>
    /// Mirrors Kotlin's <c>rememberTopAppBarState(...)</c> — allocates
    /// a <see cref="TopAppBarState"/> via
    /// <see cref="Remember{T}(IComposer, Func{T}, int, string)"/>
    /// so the same instance survives recompositions, with the three
    /// initial offsets surfaced as named C# parameters with Kotlin's
    /// defaults.
    /// </summary>
    public static TopAppBarState RememberTopAppBarState(
        this IComposer composer,
        float initialHeightOffsetLimit = float.NegativeInfinity,
        float initialHeightOffset = 0f,
        float initialContentOffset = 0f,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "") =>
        composer.Remember(
            () => new TopAppBarState(
                initialHeightOffsetLimit, initialHeightOffset, initialContentOffset),
            line, file);

    /// <summary>
    /// Mirrors Kotlin's
    /// <c>TopAppBarDefaults.pinnedScrollBehavior(state)</c>. The bar
    /// stays in place but tracks the content's scroll offset on
    /// <paramref name="state"/> so it can raise its surface elevation
    /// when content scrolls under it.
    /// </summary>
    public static ITopAppBarScrollBehavior PinnedScrollBehavior(
        this IComposer composer,
        TopAppBarState state,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(state);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            // canScroll: null → Kotlin's `() -> true` default; bit 1 in
            // `$default` flags that we're omitting it.
            return AndroidX.Compose.Material3.TopAppBarDefaults.Instance
                .PinnedScrollBehavior(state, canScroll: null, composer, 0, 0b10);
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }

    /// <summary>
    /// Mirrors Kotlin's
    /// <c>TopAppBarDefaults.enterAlwaysScrollBehavior(state)</c>.
    /// </summary>
    public static ITopAppBarScrollBehavior EnterAlwaysScrollBehavior(
        this IComposer composer,
        TopAppBarState state,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "") =>
        InvokeScrollBehavior(composer, state, line, file, ComposeBridges.TopAppBarDefaultsEnterAlwaysScrollBehavior);

    /// <summary>
    /// Mirrors Kotlin's
    /// <c>TopAppBarDefaults.exitUntilCollapsedScrollBehavior(state)</c>.
    /// </summary>
    public static ITopAppBarScrollBehavior ExitUntilCollapsedScrollBehavior(
        this IComposer composer,
        TopAppBarState state,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "") =>
        InvokeScrollBehavior(composer, state, line, file, ComposeBridges.TopAppBarDefaultsExitUntilCollapsedScrollBehavior);

    static ITopAppBarScrollBehavior InvokeScrollBehavior(
        IComposer composer,
        TopAppBarState state, int line, string file,
        Func<TopAppBarState, IComposer, IntPtr> bridge)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(state);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            IntPtr handle = bridge(state, composer);
            try
            {
                return Java.Lang.Object.GetObject<ITopAppBarScrollBehavior>(
                    handle, JniHandleOwnership.TransferLocalRef)!;
            }
            catch
            {
                if (handle != IntPtr.Zero)
                    JNIEnv.DeleteLocalRef(handle);
                throw;
            }
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }
}
