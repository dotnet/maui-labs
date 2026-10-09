using Android.Runtime;
using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

// Hand-written JNI bridges for Kotlin synthetic `$default` overloads
// that yield a Material 3 ColorScheme. Used by
// MaterialTheme.LightColorScheme(...) / MaterialTheme.DarkColorScheme(...)
// so callers can take the Material 3 baseline palette and optionally
// override individual slots, without having to supply all 48 Color
// values themselves.
//
// `ColorSchemeKt.lightColorScheme(J×48)` and `darkColorScheme(J×48)`
// ARE bound directly — `Color` is `@JvmInline value class Color(val value: ULong)`
// which the binder represents as plain `long`, so the JVM names aren't
// stripped. What ISN'T bound is the synthetic `$default` sibling
// (Kotlin emits it for any function whose parameters have default
// values), so a managed caller currently has to pass all 48 colors
// explicitly. These bridges invoke the synthetic with a dual-int
// `$default` mask: bit N == 1 tells Kotlin "use the per-slot default
// from the M3 tonal palette tokens"; bit N == 0 tells it "use the
// caller-supplied long at slot N".
//
// Why raw JNI instead of `[ComposeBridge]`: the generator's
// non-`@Composable` shape supports a single `int $default` slot.
// `lightColorScheme$default` has 48 colors, requiring TWO `$default`
// ints (32 bits each), which the generator doesn't model.
internal static partial class ComposeBridges
{
    // 48 J slots (one per Color param) + II (two $default mask ints,
    // since 48 > 32 bits) + L (synthetic-overload Object marker).
    const string ColorSchemeDefaultSig =
        "(JJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJJ" +
        "IILjava/lang/Object;)" +
        "Landroidx/compose/material3/ColorScheme;";

    // Number of Color slots in lightColorScheme / darkColorScheme.
    internal const int ColorSchemeSlotCount = 48;

    static IntPtr s_lightColorSchemeDefault_class;
    static IntPtr s_lightColorSchemeDefault_method;
    static IntPtr s_darkColorSchemeDefault_class;
    static IntPtr s_darkColorSchemeDefault_method;

    /// <summary>
    /// Build a Material 3 light <see cref="ColorScheme"/> using every
    /// per-slot default from the M3 tonal palette tokens.
    /// </summary>
    internal static ColorScheme DefaultLightColorScheme() => InvokeColorSchemeDefault(
        ref s_lightColorSchemeDefault_class,
        ref s_lightColorSchemeDefault_method,
        "lightColorScheme-_VG5OTI$default",
        colors: null,
        mask0: -1,
        mask1: -1);

    /// <summary>
    /// Build a Material 3 dark <see cref="ColorScheme"/> using every
    /// per-slot default from the M3 tonal palette tokens.
    /// </summary>
    internal static ColorScheme DefaultDarkColorScheme() => InvokeColorSchemeDefault(
        ref s_darkColorSchemeDefault_class,
        ref s_darkColorSchemeDefault_method,
        "darkColorScheme-_VG5OTI$default",
        colors: null,
        mask0: -1,
        mask1: -1);

    /// <summary>
    /// Build a Material 3 light <see cref="ColorScheme"/> from a
    /// 48-element <paramref name="colors"/> array of packed
    /// <see cref="Color"/> longs and a dual-int <paramref name="mask0"/> /
    /// <paramref name="mask1"/> selecting which slots use the
    /// caller-supplied value (bit clear) vs the Kotlin per-slot
    /// default (bit set).
    /// </summary>
    internal static ColorScheme CustomLightColorScheme(long[] colors, int mask0, int mask1) =>
        InvokeColorSchemeDefault(
            ref s_lightColorSchemeDefault_class,
            ref s_lightColorSchemeDefault_method,
            "lightColorScheme-_VG5OTI$default",
            colors: colors,
            mask0: mask0,
            mask1: mask1);

    /// <summary>
    /// Dark-mode counterpart of <see cref="CustomLightColorScheme"/>.
    /// </summary>
    internal static ColorScheme CustomDarkColorScheme(long[] colors, int mask0, int mask1) =>
        InvokeColorSchemeDefault(
            ref s_darkColorSchemeDefault_class,
            ref s_darkColorSchemeDefault_method,
            "darkColorScheme-_VG5OTI$default",
            colors: colors,
            mask0: mask0,
            mask1: mask1);

    static unsafe ColorScheme InvokeColorSchemeDefault(
        ref IntPtr classCache,
        ref IntPtr methodCache,
        string jvmName,
        long[]? colors,
        int mask0,
        int mask1)
    {
        if (methodCache == IntPtr.Zero)
        {
            classCache = JNIEnv.FindClass("androidx/compose/material3/ColorSchemeKt");
            methodCache = JNIEnv.GetStaticMethodID(classCache, jvmName, ColorSchemeDefaultSig);
        }

        // 48 colors + 2 mask ints + 1 marker = 51 JValue slots.
        JValue* args = stackalloc JValue[51];
        if (colors is null)
        {
            for (int i = 0; i < ColorSchemeSlotCount; i++)
                args[i] = new JValue(0L);
        }
        else
        {
            if (colors.Length != ColorSchemeSlotCount)
                throw new ArgumentException(
                    $"colors array must have exactly {ColorSchemeSlotCount} elements; got {colors.Length}.",
                    nameof(colors));
            for (int i = 0; i < ColorSchemeSlotCount; i++)
                args[i] = new JValue(colors[i]);
        }
        // mask0 covers slots 0-31, mask1 covers slots 32-47. Bit N == 1
        // tells Kotlin "use the per-slot default from the M3 tonal
        // palette tokens"; bit N == 0 tells it "use the caller-supplied
        // long at slot N". Bits past position 47 (the last real
        // parameter) are never tested by the generated $default body.
        args[48] = new JValue(mask0);
        args[49] = new JValue(mask1);
        args[50] = new JValue(IntPtr.Zero);

        IntPtr handle = JNIEnv.CallStaticObjectMethod(classCache, methodCache, args);
        return Java.Lang.Object.GetObject<ColorScheme>(handle, JniHandleOwnership.TransferLocalRef)!;
    }
}
