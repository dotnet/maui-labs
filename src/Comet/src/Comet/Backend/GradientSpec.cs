#nullable enable
namespace Comet
{
	/// <summary>Axis of a linear gradient. The gold apps use exactly these three:
	/// horizontal (Jetsnack fills), vertical (Jetcaster scrims, JetLagged
	/// backgrounds), and top-leading→bottom-trailing diagonal (Jetsnack borders).</summary>
	public enum GradientDirection
	{
		Horizontal,
		Vertical,
		Diagonal,
		/// <summary>Center-out radial (Jetcaster's radialGradientScrim home background).</summary>
		Radial,
	}

	/// <summary>
	/// The single gradient wire shape carried by <c>GradientBackground</c> /
	/// <c>GradientBorder</c> typed patches (v2 of the original bare Color[] payload —
	/// defined before more callers baked the array shape in).
	///
	/// <para><see cref="Stops"/> are spaced evenly along <see cref="Direction"/> and
	/// carry per-stop alpha in their ARGB (Jetcaster's eased scrims precompute their
	/// decay into N stops app-side).</para>
	///
	/// <para><see cref="ExtentDp"/>/<see cref="OffsetDp"/>/<see cref="Mirror"/> model the
	/// gold's <c>offsetGradientBackground</c> parallax: the gradient spans
	/// <c>ExtentDp</c> along the axis starting at <c>-OffsetDp</c> (instead of filling
	/// the node bounds), tiling mirrored beyond it. Android-first; the SwiftUI shim
	/// renders direction + stops and documents extent/offset as a deviation until a
	/// consumer needs it there.</para>
	/// </summary>
	public sealed record GradientSpec(
		Microsoft.Maui.Graphics.Color[] Stops,
		GradientDirection Direction = GradientDirection.Horizontal,
		float? ExtentDp = null,
		float OffsetDp = 0f,
		bool Mirror = false);
}
