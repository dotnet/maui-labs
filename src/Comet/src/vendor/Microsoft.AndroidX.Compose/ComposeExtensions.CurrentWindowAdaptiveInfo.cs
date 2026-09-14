using AndroidX.Compose.Material3.Adaptive;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    /// <summary>
    /// C# parity of Kotlin's <c>currentWindowAdaptiveInfo()</c> <c>@Composable</c>
    /// from <c>androidx.compose.material3.adaptive</c>. Returns the live
    /// <see cref="WindowAdaptiveInfo"/> for the host window — exposes both the
    /// upstream <see cref="AndroidX.Window.Core.Layout.WindowSizeClass"/>
    /// (with <c>IsWidthAtLeastBreakpoint</c> / <c>IsHeightAtLeastBreakpoint</c>
    /// + the standard 600 / 840 / 480 / 900 dp breakpoint constants) and the
    /// <see cref="Posture"/> describing foldable / tabletop state.
    ///
    /// <para>Use this to branch between phone / tablet / desktop layouts inside
    /// a composable:</para>
    ///
    /// <code>
    /// var info = composer.CurrentWindowAdaptiveInfo();
    /// if (info.WindowSizeClass.IsWidthAtLeastBreakpoint(
    ///         AndroidX.Window.Core.Layout.WindowSizeClass.WidthDpMediumLowerBound))
    ///     return ListDetailScreen.Build(...);
    /// else
    ///     return PhoneScreen.Build(...);
    /// </code>
    ///
    /// <para>Re-reads on every recomposition Compose drives, so layout code
    /// that branches off the returned <see cref="WindowAdaptiveInfo"/> reacts
    /// naturally to rotation, multi-window resize, fold / unfold, and
    /// activity-embedded surface changes.</para>
    /// </summary>
    /// <param name="composer">The composer for the active composition.</param>
    /// <param name="supportLargeAndXLargeWidth">
    /// When <c>true</c>, snaps width buckets at the additional <c>1200</c> dp
    /// (large) and <c>1600</c> dp (extra-large) breakpoints in addition to the
    /// standard <c>600</c> / <c>840</c> bounds. Mirrors the Kotlin parameter
    /// of the same name; defaults to <c>false</c> for parity with the
    /// no-argument Kotlin call site.
    /// </param>
    /// <returns>
    /// The bound <see cref="WindowAdaptiveInfo"/> Kotlin would have returned —
    /// not a managed wrapper. Pass it straight to any binding API that takes
    /// one (e.g. <c>NavigationSuiteScaffold</c>).
    /// </returns>
    public static WindowAdaptiveInfo CurrentWindowAdaptiveInfo(
        this IComposer composer,
        bool supportLargeAndXLargeWidth = false)
    {
        ArgumentNullException.ThrowIfNull(composer);

        return WindowAdaptiveInfoKt.CurrentWindowAdaptiveInfo(
            supportLargeAndXLargeWidth,
            composer,
            p2:       0,
            _changed: 0);
    }
}
