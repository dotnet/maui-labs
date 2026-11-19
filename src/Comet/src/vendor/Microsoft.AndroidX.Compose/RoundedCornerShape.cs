using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// <c>androidx.compose.foundation.shape.RoundedCornerShape</c> as a
/// first-class <see cref="Shape"/> value. Pass an instance to any
/// facade's <c>Shape</c> slot — <c>Card</c>, <c>Surface</c>,
/// <c>Button</c>, <c>TextField</c>, <c>AlertDialog</c>, the chip
/// family, the FAB family, <c>ModalBottomSheet</c>, and so on.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the Kotlin overload set. <see cref="Dp"/>-taking ctors
/// produce density-aware radii (a 16dp corner stays 16dp regardless of
/// screen density). <see cref="int"/>-taking ctors take a percentage of
/// the shorter side — <c>0</c> = square, <c>50</c> = pill / circle.
/// </para>
/// <para>
/// <b>Overload tip:</b> <c>new RoundedCornerShape(16)</c> binds to the
/// percent ctor (matching Kotlin's bare-<c>Int</c> convention); use
/// <c>new RoundedCornerShape(16.Dp())</c> or
/// <c>new RoundedCornerShape(new Dp(16))</c> for a 16dp radius.
/// </para>
/// </remarks>
public sealed class RoundedCornerShape : Shape
{
    /// <summary>
    /// Equal radius on all four corners, expressed in density-
    /// independent pixels.
    /// </summary>
    public RoundedCornerShape(Dp cornerSize)
        : base(ComposeBridges.RoundedCornerShape(cornerSize.Value),
               JniHandleOwnership.TransferLocalRef)
    {
    }

    /// <summary>
    /// Equal radius on all four corners, expressed as a percentage
    /// (0–50) of the shorter side. <c>0</c> renders a square,
    /// <c>50</c> renders a circle (square bounds) or pill (rectangular
    /// bounds).
    /// </summary>
    public RoundedCornerShape(int cornerPercent)
        : base(ComposeBridges.RoundedCornerShapePercent(cornerPercent),
               JniHandleOwnership.TransferLocalRef)
    {
    }

    /// <summary>
    /// Independent radii per corner, expressed in density-independent
    /// pixels. Argument order matches Kotlin: top-start, top-end,
    /// bottom-end, bottom-start — clockwise from the top-leading
    /// corner.
    /// </summary>
    /// <remarks>
    /// The canonical chat-bubble shape uses one flattened corner to
    /// connect to its avatar tile — e.g.
    /// <c>new RoundedCornerShape(4.Dp(), 20.Dp(), 20.Dp(), 20.Dp())</c>
    /// for a bubble whose top-leading corner is squared off where it
    /// meets the speaker's avatar.
    /// </remarks>
    public RoundedCornerShape(Dp topStart, Dp topEnd, Dp bottomEnd, Dp bottomStart)
        : base(ComposeBridges.RoundedCornerShape4(
                   topStart.Value, topEnd.Value, bottomEnd.Value, bottomStart.Value),
               JniHandleOwnership.TransferLocalRef)
    {
    }

    /// <summary>
    /// Independent percent radii per corner. Argument order matches
    /// Kotlin: top-start, top-end, bottom-end, bottom-start (clockwise
    /// from the top-leading corner). Each value is a percentage (0–50)
    /// of the shorter side.
    /// </summary>
    public RoundedCornerShape(int topStartPercent, int topEndPercent,
                              int bottomEndPercent, int bottomStartPercent)
        : base(ComposeBridges.RoundedCornerShape4Percent(
                   topStartPercent, topEndPercent, bottomEndPercent, bottomStartPercent),
               JniHandleOwnership.TransferLocalRef)
    {
    }
}
