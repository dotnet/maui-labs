#nullable enable
using System;
using System.Windows.Controls;
using Microsoft.Maui.Graphics;
using WRect = global::System.Windows.Rect;
using WSize = global::System.Windows.Size;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	public class LayoutPanel : Panel
	{
		internal Func<double, double, Size>? CrossPlatformMeasure { get; set; }
		internal Func<Rect, Size>? CrossPlatformArrange { get; set; }
		internal ILayout? VirtualView { get; set; }

		public bool ClipsToBounds { get; set; }

		public LayoutPanel()
		{
		}

		protected override WSize MeasureOverride(WSize availableSize)
		{
			if (CrossPlatformMeasure == null)
			{
				return base.MeasureOverride(availableSize);
			}

			var width = availableSize.Width;
			var height = availableSize.Height;

			var crossPlatformSize = CrossPlatformMeasure(width, height);

			width = crossPlatformSize.Width;
			height = crossPlatformSize.Height;

			// Use the cross-platform result to constrain child measurement.
			// When available size is ∞ (e.g. inside ScrollView or CollectionView),
			// use the MAUI-calculated size as the constraint so children don't
			// report inflated desired sizes.
			var constrainedSize = new WSize(
				double.IsInfinity(availableSize.Width) ? width : Math.Max(width, availableSize.Width),
				double.IsInfinity(availableSize.Height) ? height : Math.Max(height, availableSize.Height));

			// WPF requires all children to be measured during MeasureOverride.
			// MAUI's CrossPlatformMeasure may not call WPF Measure on all children
			// (e.g., views with explicit WidthRequest/HeightRequest).
			double maxChildHeight = 0;
			for (var index = 0; index < InternalChildren.Count; index++)
			{
				var child = InternalChildren[index];
				// Keep the constraints chosen by MAUI for children it already measured.
				// Remeasuring a FlexLayout with the whole parent's height inflates its lines.
				// Nested flex items are an exception: the shared flex engine bypasses
				// their native Measure, so they still need the fallback on constraint changes.
				var nestedFlex = VirtualView is IFlexLayout flex && index < flex.Count && flex[index] is IFlexLayout;
				if (!child.IsMeasureValid || nestedFlex)
					child.Measure(constrainedSize);
				if (child.DesiredSize.Height > maxChildHeight)
					maxChildHeight = child.DesiredSize.Height;
			}

			// Ensure the panel is at least as tall as its tallest child.
			// FlexLayout.CrossPlatformMeasure can underreport height when children
			// have explicit WidthRequest/HeightRequest (MAUI skips GetDesiredSize).
			if (maxChildHeight > height && !double.IsInfinity(maxChildHeight))
				height = maxChildHeight;

			return new WSize(width, height);
		}

		protected override WSize ArrangeOverride(WSize finalSize)
		{
			if (CrossPlatformArrange == null)
			{
				return base.ArrangeOverride(finalSize);
			}

			var width = finalSize.Width;
			var height = finalSize.Height;

			CrossPlatformArrange(new Rect(0, 0, width, height));

			return finalSize;
		}
	}
}
