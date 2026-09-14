using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.graphics.Shape</c>.
/// Compose's <c>Shape</c> is an interface; concrete shapes like
/// <c>RoundedCornerShape</c> live in <c>ui-graphics-android</c>, which
/// is bound by <see href="https://github.com/dotnet/android-libraries/commit/da8b14bfd2275ba8c53d999fda48dda97476ee37"/>
/// in newer NuGets. Until we adopt that bump, this thin wrapper holds
/// a Kotlin <c>Shape</c> handle so the bridge generator's reference-
/// type code path can pass it to <c>L</c> JNI slots.
///
/// Use the factory <see cref="RoundedCorners(Dp)"/> to build a
/// <c>RoundedCornerShape</c> from a corner-radius <see cref="Dp"/>;
/// the factory delegates to the existing
/// <c>ComposeBridges.RoundedCornerShape</c> bridge.
/// </summary>
public class Shape : Java.Lang.Object
{
    private protected Shape(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary>
    /// <c>androidx.compose.foundation.shape.RoundedCornerShape(corner: Dp)</c>
    /// — equal radius on all four corners.
    /// </summary>
    public static Shape RoundedCorners(Dp cornerRadius)
    {
        IntPtr handle = ComposeBridges.RoundedCornerShape(cornerRadius.Value);
        return new Shape(handle, JniHandleOwnership.TransferLocalRef);
    }

    /// <summary>
    /// <c>androidx.compose.foundation.shape.RoundedCornerShape(topStart, topEnd, bottomEnd, bottomStart)</c>
    /// — independent radii per corner expressed in <see cref="Dp"/>. The
    /// argument order matches Kotlin: top-start, top-end, bottom-end,
    /// bottom-start (clockwise from the top-leading corner). Pass
    /// <c>0.Dp()</c> for a square corner.
    /// </summary>
    /// <remarks>
    /// The canonical "chat bubble" shape uses one flattened corner to
    /// visually connect to its avatar tile — e.g.
    /// <c>Shape.RoundedCorners(4, 20, 20, 20)</c> for a bubble whose
    /// top-leading corner is squared off where it meets the speaker's
    /// avatar.
    /// </remarks>
    public static Shape RoundedCorners(Dp topStart, Dp topEnd, Dp bottomEnd, Dp bottomStart)
    {
        IntPtr handle = ComposeBridges.RoundedCornerShape4(
            topStart.Value, topEnd.Value, bottomEnd.Value, bottomStart.Value);
        return new Shape(handle, JniHandleOwnership.TransferLocalRef);
    }

    /// <summary>
    /// <c>androidx.compose.foundation.shape.RoundedCornerShape(percent: Int)</c>
    /// — equal radius on all four corners, expressed as a percentage of
    /// the smaller dimension. <c>0</c> = rectangle, <c>50</c> = pill /
    /// circle (use <see cref="Circle"/> for the canonical name).
    /// </summary>
    public static Shape RoundedPercent(int percent)
    {
        IntPtr handle = ComposeBridges.RoundedCornerShapePercent(percent);
        return new Shape(handle, JniHandleOwnership.TransferLocalRef);
    }

    /// <summary>
    /// <c>RoundedCornerShape(50)</c> — fully rounded, producing a
    /// circle (when the bounds are square) or a pill (when they're
    /// rectangular).
    /// </summary>
    public static Shape Circle() => RoundedPercent(50);

    /// <summary>
    /// <c>androidx.compose.foundation.shape.CutCornerShape(size: Dp)</c>
    /// — chamfered (straight-line) corners on all four sides.
    /// </summary>
    public static Shape CutCorners(Dp cornerSize)
    {
        IntPtr handle = ComposeBridges.CutCornerShapeDp(cornerSize.Value);
        return new Shape(handle, JniHandleOwnership.TransferLocalRef);
    }

    /// <summary>
    /// <c>androidx.compose.foundation.shape.CutCornerShape(percent: Int)</c>
    /// — chamfered corners expressed as a percentage of the smaller
    /// dimension.
    /// </summary>
    public static Shape CutCornersPercent(int percent)
    {
        IntPtr handle = ComposeBridges.CutCornerShapePercent(percent);
        return new Shape(handle, JniHandleOwnership.TransferLocalRef);
    }

    /// <summary>
    /// <c>androidx.compose.ui.graphics.RectangleShape</c> — Compose's
    /// canonical "no clipping, just a rectangle" shape singleton. Pass
    /// this anywhere a <see cref="Shape"/> is expected when you want
    /// the default (rectangular) outline — useful when overriding a
    /// facade's default rounded shape, or when supplying a
    /// <c>Modifier.Background(Brush, Shape)</c> with no rounding.
    /// </summary>
    /// <remarks>
    /// The underlying Kotlin value is a singleton, so this property
    /// always returns the same Java peer (held as a global ref behind
    /// the scenes).
    /// </remarks>
    public static Shape Rectangle { get; } = RectangleShapeFactory();

    static Shape RectangleShapeFactory()
    {
        // `RectangleShapeKt.RectangleShape` is bound — it returns the
        // shared Kotlin singleton. Wrap its handle in our facade type
        // without transferring ownership; the bound peer keeps the
        // underlying JNI ref alive.
        var bound = AndroidX.Compose.UI.Graphics.RectangleShapeKt.RectangleShape;
        try
        {
            return new Shape(
                ((Java.Lang.Object)bound).Handle,
                JniHandleOwnership.DoNotTransfer);
        }
        finally
        {
            GC.KeepAlive(bound);
        }
    }
}
