using Android.Runtime;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Graphics.Painter;
using AndroidX.Compose.UI.Res;

namespace AndroidX.Compose;

/// <summary>
/// Composable resource accessors — the C# parity of Kotlin's
/// <c>stringResource</c> / <c>colorResource</c> / <c>dimensionResource</c> /
/// <c>painterResource</c> family from <c>androidx.compose.ui.res</c>.
/// Every method is an extension on <see cref="IComposer"/> so the active
/// composer is passed explicitly — exactly the <c>$composer</c> shape
/// Kotlin's IR rewrite uses.
/// </summary>
public static partial class ComposeExtensions
{
    /// <summary>
    /// Load a string resource. Mirrors Kotlin's
    /// <c>stringResource(@StringRes id: Int): String</c>.
    /// </summary>
    public static string StringResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return StringResources_androidKt.StringResource(id, composer, _changed: 0);
    }

    /// <summary>
    /// Load a string resource with <c>String.format</c>-style arguments.
    /// Mirrors Kotlin's
    /// <c>stringResource(@StringRes id: Int, vararg formatArgs: Any): String</c>.
    /// </summary>
    public static string StringResource(this IComposer composer, int id, params object?[] formatArgs)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(formatArgs);
        var (boxed, owned) = BoxArgs(formatArgs);
        try
        {
            return StringResources_androidKt.StringResource(id, boxed, composer, _changed: 0);
        }
        finally
        {
            DisposeOwned(boxed, owned);
        }
    }

    /// <summary>
    /// Load a string-array resource. Mirrors Kotlin's
    /// <c>stringArrayResource(@ArrayRes id: Int): Array&lt;String&gt;</c>.
    /// </summary>
    public static string[] StringArrayResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return StringResources_androidKt.StringArrayResource(id, composer, _changed: 0);
    }

    /// <summary>
    /// Load a plurals resource. Mirrors Kotlin's
    /// <c>pluralStringResource(@PluralsRes id: Int, count: Int): String</c>.
    /// </summary>
    public static string PluralStringResource(this IComposer composer, int id, int count)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return StringResources_androidKt.PluralStringResource(id, count, composer, _changed: 0);
    }

    /// <summary>
    /// Load a plurals resource with <c>String.format</c>-style arguments. Mirrors
    /// Kotlin's <c>pluralStringResource(@PluralsRes id: Int, count: Int, vararg formatArgs: Any): String</c>.
    /// </summary>
    public static string PluralStringResource(this IComposer composer, int id, int count, params object?[] formatArgs)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(formatArgs);
        var (boxed, owned) = BoxArgs(formatArgs);
        try
        {
            return StringResources_androidKt.PluralStringResource(id, count, boxed, composer, _changed: 0);
        }
        finally
        {
            DisposeOwned(boxed, owned);
        }
    }

    /// <summary>
    /// Load an integer resource. Mirrors Kotlin's
    /// <c>integerResource(@IntegerRes id: Int): Int</c>.
    /// </summary>
    public static int IntegerResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return PrimitiveResources_androidKt.IntegerResource(id, composer, _changed: 0);
    }

    /// <summary>
    /// Load an integer-array resource. Mirrors Kotlin's
    /// <c>integerArrayResource(@ArrayRes id: Int): IntArray</c>.
    /// </summary>
    public static int[] IntegerArrayResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return PrimitiveResources_androidKt.IntegerArrayResource(id, composer, _changed: 0);
    }

    /// <summary>
    /// Load a boolean resource. Mirrors Kotlin's
    /// <c>booleanResource(@BoolRes id: Int): Boolean</c>.
    /// </summary>
    public static bool BooleanResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return PrimitiveResources_androidKt.BooleanResource(id, composer, _changed: 0);
    }

    /// <summary>
    /// Load a dimension resource as a <see cref="Dp"/>. Mirrors Kotlin's
    /// <c>dimensionResource(@DimenRes id: Int): Dp</c>.
    /// </summary>
    public static Dp DimensionResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        float raw = PrimitiveResources_androidKt.DimensionResource(id, composer, _changed: 0);
        return new Dp(raw);
    }

    /// <summary>
    /// Load a color resource as a packed Compose <c>Color</c> long. Mirrors
    /// Kotlin's <c>colorResource(@ColorRes id: Int): Color</c>.
    /// </summary>
    public static long ColorResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return ColorResources_androidKt.ColorResource(id, composer, _changed: 0);
    }

    /// <summary>
    /// Load a drawable resource as a <see cref="Painter"/>. Mirrors Kotlin's
    /// <c>painterResource(@DrawableRes id: Int): Painter</c>.
    /// </summary>
    public static Painter PainterResource(this IComposer composer, int id)
    {
        ArgumentNullException.ThrowIfNull(composer);
        IntPtr handle = ComposeBridges.PainterResource(id, composer);
        return Java.Lang.Object.GetObject<Painter>(handle, JniHandleOwnership.TransferLocalRef)
            ?? throw new InvalidOperationException(
                $"painterResource({id}) returned a null Painter handle.");
    }

    // Convert a C# format-args array into a Java.Lang.Object[] suitable for
    // crossing JNI. Returns a parallel bool[] flagging which entries we
    // allocated ourselves so DisposeOwned can release them after the call.
    static (Java.Lang.Object[] boxed, bool[] owned) BoxArgs(object?[] args)
    {
        var boxed = new Java.Lang.Object[args.Length];
        var owned = new bool[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case null:
                    boxed[i] = null!;
                    owned[i] = false;
                    break;
                case Java.Lang.Object o:
                    boxed[i] = o;
                    owned[i] = false;
                    break;
                case string s:           boxed[i] = new Java.Lang.String(s);          owned[i] = true; break;
                case bool b:             boxed[i] = Java.Lang.Boolean.ValueOf(b)!;     owned[i] = true; break;
                case char c:             boxed[i] = Java.Lang.Character.ValueOf(c)!;   owned[i] = true; break;
                case sbyte sb:           boxed[i] = Java.Lang.Byte.ValueOf(sb)!;       owned[i] = true; break;
                case byte by:            boxed[i] = Java.Lang.Short.ValueOf((short)by)!; owned[i] = true; break;
                case short sh:           boxed[i] = Java.Lang.Short.ValueOf(sh)!;      owned[i] = true; break;
                case ushort us:          boxed[i] = Java.Lang.Integer.ValueOf(us)!;    owned[i] = true; break;
                case int ii:             boxed[i] = Java.Lang.Integer.ValueOf(ii)!;    owned[i] = true; break;
                case uint ui:            boxed[i] = Java.Lang.Long.ValueOf(ui)!;       owned[i] = true; break;
                case long l:             boxed[i] = Java.Lang.Long.ValueOf(l)!;        owned[i] = true; break;
                case ulong ul:           boxed[i] = Java.Lang.Long.ValueOf(unchecked((long)ul))!; owned[i] = true; break;
                case float f:            boxed[i] = Java.Lang.Float.ValueOf(f)!;       owned[i] = true; break;
                case double d:           boxed[i] = Java.Lang.Double.ValueOf(d)!;      owned[i] = true; break;
                default:                 boxed[i] = new Java.Lang.String(args[i]!.ToString() ?? string.Empty); owned[i] = true; break;
            }
        }
        return (boxed, owned);
    }

    static void DisposeOwned(Java.Lang.Object[] boxed, bool[] owned)
    {
        for (int i = 0; i < boxed.Length; i++)
        {
            if (owned[i]) boxed[i]?.Dispose();
        }
    }
}
