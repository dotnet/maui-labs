using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;

namespace AndroidX.Compose;

/// <summary>
/// The <see cref="AndroidX.Lifecycle.ILifecycleOwner"/> that hosts the
/// current composition (typically the host activity or fragment). Use
/// this to scope coroutines / effects to the same lifecycle as the UI
/// tree. Equivalent to Kotlin's
/// <c>androidx.compose.ui.platform.LocalLifecycleOwner</c>.
/// </summary>
/// <remarks>
/// We bind the legacy
/// <c>androidx.compose.ui.platform.LocalLifecycleOwner</c> here instead
/// of the newer <c>androidx.lifecycle.compose.LocalLifecycleOwner</c>
/// because the lifecycle-runtime-compose binding currently strips that
/// type. The legacy accessor is marked <c>@Deprecated</c> in Kotlin but
/// still delegates to the same composition local at runtime — swap once
/// the upstream binding exposes it.
/// </remarks>
public static class LocalLifecycleOwner
{
#pragma warning disable CS0618 // legacy LocalLifecycleOwner, see <remarks>
    static readonly CompositionLocal<AndroidX.Lifecycle.ILifecycleOwner> s_instance =
        new(AndroidCompositionLocals_androidKt.LocalLifecycleOwner);
#pragma warning restore CS0618

    /// <summary>
    /// Read the current value, equivalent to Kotlin's
    /// <c>LocalLifecycleOwner.current</c>.
    /// </summary>
    public static AndroidX.Lifecycle.ILifecycleOwner Current(IComposer composer) =>
        s_instance.Current(composer);

    /// <summary>
    /// Pair this local with <paramref name="value"/> for installation
    /// by a <see cref="CompositionLocalProvider"/>.
    /// </summary>
    public static ProvidedValue Provides(AndroidX.Lifecycle.ILifecycleOwner value) =>
        s_instance.Provides(value);
}
