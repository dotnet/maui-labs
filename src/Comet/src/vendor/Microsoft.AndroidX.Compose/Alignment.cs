using Android.Runtime;
using AndroidX.Compose.UI;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around the <c>androidx.compose.ui.Alignment</c> singletons
/// exposed by Kotlin's <c>Alignment.Companion</c> object. Each public
/// static property forwards to the bound
/// <c>AndroidX.Compose.UI.IAlignment.Companion</c> singleton and caches
/// the wrapper for the lifetime of the process.
///
/// Use these with <see cref="Modifier.Align(Alignment)"/>
/// inside a <see cref="Box"/> child to position the child within the
/// parent. Use the nested <see cref="Vertical"/> singletons inside a
/// <see cref="Row"/> and the nested <see cref="Horizontal"/> singletons
/// inside a <see cref="Column"/>.
/// </summary>
/// <remarks>
/// Inherits <see cref="Java.Lang.Object"/> so the bridge generator's
/// reference-type code path
/// (<c>((Java.Lang.Object)alignment).Handle</c>) can lower an
/// <see cref="Alignment"/> instance to its underlying Kotlin
/// <c>Alignment</c> JNI handle — used by facades like
/// <see cref="Image"/> that take an optional <c>alignment</c> slot.
/// </remarks>
public sealed class Alignment : Java.Lang.Object
{
    Alignment(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    // Wrap the bound IAlignment peer's JNI handle in a brand-new
    // Java.Lang.Object peer so casts to Java.Lang.Object on this
    // instance return our own (stable) handle. Same approach as Shape.
    static Alignment From(IAlignment java)
    {
        IntPtr handle = ((Java.Lang.Object)java).Handle;
        return new Alignment(JNIEnv.NewLocalRef(handle), JniHandleOwnership.TransferLocalRef);
    }

    static Alignment? s_topStart, s_topCenter, s_topEnd, s_centerStart, s_center, s_centerEnd, s_bottomStart, s_bottomCenter, s_bottomEnd;

    /// <summary><c>Alignment.TopStart</c> — top-left in LTR, top-right in RTL.</summary>
    public static Alignment TopStart => s_topStart ??= From(IAlignment.Companion.TopStart);
    /// <summary><c>Alignment.TopCenter</c> — top-center.</summary>
    public static Alignment TopCenter => s_topCenter ??= From(IAlignment.Companion.TopCenter);
    /// <summary><c>Alignment.TopEnd</c> — top-right in LTR, top-left in RTL.</summary>
    public static Alignment TopEnd => s_topEnd ??= From(IAlignment.Companion.TopEnd);
    /// <summary><c>Alignment.CenterStart</c> — vertically centered, leading edge.</summary>
    public static Alignment CenterStart => s_centerStart ??= From(IAlignment.Companion.CenterStart);
    /// <summary><c>Alignment.Center</c> — centered both axes.</summary>
    public static Alignment Center => s_center ??= From(IAlignment.Companion.Center);
    /// <summary><c>Alignment.CenterEnd</c> — vertically centered, trailing edge.</summary>
    public static Alignment CenterEnd => s_centerEnd ??= From(IAlignment.Companion.CenterEnd);
    /// <summary><c>Alignment.BottomStart</c> — bottom-left in LTR, bottom-right in RTL.</summary>
    public static Alignment BottomStart => s_bottomStart ??= From(IAlignment.Companion.BottomStart);
    /// <summary><c>Alignment.BottomCenter</c> — bottom-center.</summary>
    public static Alignment BottomCenter => s_bottomCenter ??= From(IAlignment.Companion.BottomCenter);
    /// <summary><c>Alignment.BottomEnd</c> — bottom-right in LTR, bottom-left in RTL.</summary>
    public static Alignment BottomEnd => s_bottomEnd ??= From(IAlignment.Companion.BottomEnd);

    /// <summary>
    /// Vertical-only alignment singletons (<c>Alignment.Vertical</c>) for
    /// use with <see cref="Modifier.Align(Vertical)"/>
    /// inside a <see cref="Row"/>.
    /// </summary>
    public sealed class Vertical
    {
        internal IAlignmentVertical Java { get; }
        Vertical(IAlignmentVertical java) => Java = java;

        static Vertical? s_top, s_center, s_bottom;

        /// <summary><c>Alignment.Top</c> — align to the top of the row.</summary>
        public static Vertical Top => s_top ??= new Vertical(IAlignment.Companion.Top);
        /// <summary><c>Alignment.CenterVertically</c> — align to the vertical center.</summary>
        public static Vertical CenterVertically => s_center ??= new Vertical(IAlignment.Companion.CenterVertically);
        /// <summary><c>Alignment.Bottom</c> — align to the bottom of the row.</summary>
        public static Vertical Bottom => s_bottom ??= new Vertical(IAlignment.Companion.Bottom);
    }

    /// <summary>
    /// Horizontal-only alignment singletons (<c>Alignment.Horizontal</c>)
    /// for use with <see cref="Modifier.Align(Horizontal)"/>
    /// inside a <see cref="Column"/>.
    /// </summary>
    public sealed class Horizontal
    {
        internal IAlignmentHorizontal Java { get; }
        Horizontal(IAlignmentHorizontal java) => Java = java;

        static Horizontal? s_start, s_center, s_end;

        /// <summary><c>Alignment.Start</c> — align to the leading edge.</summary>
        public static Horizontal Start => s_start ??= new Horizontal(IAlignment.Companion.Start);
        /// <summary><c>Alignment.CenterHorizontally</c> — align to the horizontal center.</summary>
        public static Horizontal CenterHorizontally => s_center ??= new Horizontal(IAlignment.Companion.CenterHorizontally);
        /// <summary><c>Alignment.End</c> — align to the trailing edge.</summary>
        public static Horizontal End => s_end ??= new Horizontal(IAlignment.Companion.End);
    }
}
