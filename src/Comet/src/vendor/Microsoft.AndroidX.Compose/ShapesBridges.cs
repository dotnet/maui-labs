using Android.Runtime;
using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

// Hand-written JNI bridge for the Material 3 `Shapes` synthetic
// default constructor. Used by MaterialTheme.Shapes(...) so callers
// can override individual shape slots (extraSmall / small / medium /
// large / extraLarge) while leaving the rest at their M3 baseline
// values.
//
// The Shapes class itself is bound, but only its parameterless
// constructor is exposed. The full primary constructor takes
// 5 CornerBasedShape params plus Kotlin's synthetic `$default` int
// + DefaultConstructorMarker. Why raw JNI instead of
// `[ComposeBridge(JvmName = "<init>")]`: the constructor bridge shape
// in the generator (CN2006) doesn't currently support a trailing
// `$default` int + Object marker. Hand-rolling it here is a
// six-line JNI call.
internal static partial class ComposeBridges
{
    const string ShapesDefaultCtorSig =
        "(Landroidx/compose/foundation/shape/CornerBasedShape;" +
        "Landroidx/compose/foundation/shape/CornerBasedShape;" +
        "Landroidx/compose/foundation/shape/CornerBasedShape;" +
        "Landroidx/compose/foundation/shape/CornerBasedShape;" +
        "Landroidx/compose/foundation/shape/CornerBasedShape;" +
        "ILkotlin/jvm/internal/DefaultConstructorMarker;)V";

    static IntPtr s_shapesCtor_class;
    static IntPtr s_shapesCtor_method;

    /// <summary>
    /// Build a Material 3 <see cref="Shapes"/> with per-slot
    /// overrides. Each <see cref="IntPtr"/> is either a
    /// <c>CornerBasedShape</c> JNI handle to use for that slot, or
    /// <see cref="IntPtr.Zero"/> to fall back to the M3
    /// baseline default for that slot via Kotlin's synthetic
    /// <c>$default</c> mechanism.
    /// </summary>
    /// <param name="defaults">
    /// 5-bit mask: bit N set means "leave slot N at the Kotlin
    /// default" (the corresponding handle is ignored). Bit 0 is
    /// extraSmall, bit 4 is extraLarge.
    /// </param>
    internal static unsafe Shapes BuildShapes(
        IntPtr extraSmall,
        IntPtr small,
        IntPtr medium,
        IntPtr large,
        IntPtr extraLarge,
        int defaults)
    {
        if (s_shapesCtor_method == IntPtr.Zero)
        {
            s_shapesCtor_class = JNIEnv.FindClass("androidx/compose/material3/Shapes");
            s_shapesCtor_method = JNIEnv.GetMethodID(s_shapesCtor_class, "<init>", ShapesDefaultCtorSig);
        }

        JValue* args = stackalloc JValue[7];
        args[0] = new JValue(extraSmall);
        args[1] = new JValue(small);
        args[2] = new JValue(medium);
        args[3] = new JValue(large);
        args[4] = new JValue(extraLarge);
        args[5] = new JValue(defaults);
        args[6] = new JValue(IntPtr.Zero); // DefaultConstructorMarker

        IntPtr handle = JNIEnv.NewObject(s_shapesCtor_class, s_shapesCtor_method, args);
        return Java.Lang.Object.GetObject<Shapes>(handle, JniHandleOwnership.TransferLocalRef)!;
    }
}
