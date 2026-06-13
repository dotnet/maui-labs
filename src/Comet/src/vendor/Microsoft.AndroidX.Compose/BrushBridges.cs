using Android.Runtime;
using BoundBrush = AndroidX.Compose.UI.Graphics.Brush;
using BoundColor = AndroidX.Compose.UI.Graphics.Color;

namespace AndroidX.Compose;

// Hand-written JNI for the two Compose-graphics symbols the
// Mono.Android binder strips:
//
//   - `androidx.compose.ui.graphics.Color.box-impl(J)Color` — the
//     Kotlin-synthetic boxing factory that turns a packed `long` into
//     a `Color` object. The `Color` class itself is bound, but its
//     ctor and `box-impl` static are skipped (value-class lowering).
//   - `androidx.compose.ui.graphics.SolidColor.<init>(J)V` — same
//     reason; the ctor takes a value-class `Color`.
//
// Everything else (`Brush.Companion`'s gradient factories,
// `RectangleShapeKt.RectangleShape`) is bound and called directly
// from `Brush` / `Shape`.
internal static partial class ComposeBridges
{
    static BoundBrush.Companion? s_brushCompanion;

    static IntPtr s_color_class;
    static IntPtr s_color_boxImpl;

    static IntPtr s_solidColor_class;
    static IntPtr s_solidColor_ctor;

    // Lazy access to the `androidx.compose.ui.graphics.Brush$Companion`
    // singleton — the binder exposes the type but not a public C# accessor
    // for the Kotlin `Companion` static field, so we fetch it via JNI
    // once and cache the bound peer. Subsequent gradient calls go
    // straight through the bound `LinearGradient_mHitzGk`/etc. methods.
    internal static BoundBrush.Companion BrushCompanion()
    {
        if (s_brushCompanion is not null) return s_brushCompanion;
        IntPtr local = IntPtr.Zero;
        try
        {
            IntPtr brushClass = Java.Lang.Class.FromType(typeof(BoundBrush)).Handle;
            IntPtr fid = JNIEnv.GetStaticFieldID(
                brushClass, "Companion",
                "Landroidx/compose/ui/graphics/Brush$Companion;");
            local = JNIEnv.GetStaticObjectField(brushClass, fid);
            return s_brushCompanion = Java.Lang.Object.GetObject<BoundBrush.Companion>(
                local, JniHandleOwnership.TransferLocalRef)!;
        }
        finally
        {
            if (local != IntPtr.Zero && s_brushCompanion is null)
                JNIEnv.DeleteLocalRef(local);
        }
    }

    // Box a packed-long Color into a bound `BoundColor` peer via the
    // Kotlin-synthetic `Color.box-impl(J)` static. Required because
    // `BoundBrush.Companion.LinearGradient_mHitzGk(IList<Color>, ...)`
    // (and every other gradient factory) takes a List of *boxed* Color
    // objects — packed longs alone aren't acceptable.
    internal static unsafe BoundColor BoxColor(long packed)
    {
        if (s_color_boxImpl == IntPtr.Zero)
        {
            s_color_class = Java.Lang.Class.FromType(typeof(BoundColor)).Handle;
            s_color_boxImpl = JNIEnv.GetStaticMethodID(
                s_color_class, "box-impl", "(J)Landroidx/compose/ui/graphics/Color;");
        }
        var args = stackalloc JValue[1];
        args[0] = new JValue(packed);
        IntPtr handle = JNIEnv.CallStaticObjectMethod(
            s_color_class, s_color_boxImpl, args);
        return Java.Lang.Object.GetObject<BoundColor>(
            handle, JniHandleOwnership.TransferLocalRef)!;
    }

    // Build an `IList<BoundColor>` from a managed `Color[]` for the
    // gradient factories. `JavaList<T>` is the Mono.Android wrapper
    // over `java.util.ArrayList` — no further JNI required for `add`.
    internal static IList<BoundColor> ToColorList(Color[] colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Length == 0)
            throw new ArgumentException(
                "Gradient must have at least one color stop.", nameof(colors));
        var list = new JavaList<BoundColor>();
        foreach (var c in colors)
            list.Add(BoxColor(c));
        return list;
    }

    // `new androidx.compose.ui.graphics.SolidColor(Color)` — the ctor
    // is stripped because its parameter is a value-class `Color`.
    internal static unsafe IntPtr BrushSolidColor(long color)
    {
        if (s_solidColor_ctor == IntPtr.Zero)
        {
            s_solidColor_class = Java.Lang.Class.FromType(
                typeof(AndroidX.Compose.UI.Graphics.SolidColor)).Handle;
            s_solidColor_ctor = JNIEnv.GetMethodID(s_solidColor_class, "<init>", "(J)V");
        }
        var args = stackalloc JValue[1];
        args[0] = new JValue(color);
        return JNIEnv.NewObject(s_solidColor_class, s_solidColor_ctor, args);
    }
}
