#nullable enable
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	internal static class ButtonMeasurementContract
	{
		const double DefaultHorizontalContentPadding = 48;
		const double DefaultVerticalContentPadding = 20;
		const double MinimumInteractiveHeight = 48;

		/// <summary>Returns the content-box measurement expected from the backend node.
		/// Yoga/Grid adds explicitly configured Comet padding around this result.</summary>
		public static Size MeasureContent(
			Size label,
			Thickness padding,
			bool hasExplicitPadding)
		{
			if (!hasExplicitPadding)
			{
				return new Size(
					label.Width + DefaultHorizontalContentPadding,
					System.Math.Max(label.Height + DefaultVerticalContentPadding, MinimumInteractiveHeight));
			}

			// Preserve the native control's minimum hit height without adding its default
			// content insets. The layout engine adds the explicit padding exactly once.
			return new Size(
				label.Width,
				System.Math.Max(label.Height, MinimumInteractiveHeight - padding.VerticalThickness));
		}
	}
}
