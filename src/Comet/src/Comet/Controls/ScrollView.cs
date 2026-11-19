using System;
using System.Collections;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui.Graphics;
using Microsoft.Maui;

namespace Comet
{
	public partial class ScrollView : ContentView, IEnumerable, IScrollView
	{
		public ScrollView(Orientation orientation = Orientation.Vertical)
		{
			Orientation = orientation;
		}

		public Orientation Orientation { get; }

		/// <summary>True while the scroll content is at the very top (scroll offset == 0); false
		/// once the user has scrolled away. The backend node drives this from the native scroll
		/// state so a floating button (e.g. ProfileFab) can reactively extend/contract.</summary>
		public Signal<bool> AtTop { get; } = new(true);

		/// <summary>The continuous vertical scroll offset in Dp (0 at the top), driven by the backend
		/// node from the native scroll state — for a parallax header that translates with the scroll
		/// (e.g. the profile photo moving at half the scroll speed).</summary>
		public Signal<double> ScrollOffset { get; } = new(0);

		ScrollBarVisibility IScrollView.HorizontalScrollBarVisibility => this.GetPropertyValue<ScrollBarVisibility?>() ?? ScrollBarVisibility.Default;

		ScrollBarVisibility IScrollView.VerticalScrollBarVisibility => this.GetPropertyValue<ScrollBarVisibility?>() ?? ScrollBarVisibility.Default;

		ScrollOrientation IScrollView.Orientation => Orientation == Orientation.Horizontal ? ScrollOrientation.Horizontal : ScrollOrientation.Vertical;

		Size IScrollView.ContentSize => Content?.MeasuredSize ?? Size.Zero;

		double IScrollView.HorizontalOffset { get; set; }
		double IScrollView.VerticalOffset { get; set; }

		public override Size GetDesiredSize(Size availableSize)
		{

			var frameConstraints = this.GetFrameConstraints();
			
			var contentMeasureSize = availableSize;

			if (frameConstraints?.Width > 0)
				contentMeasureSize.Width = frameConstraints.Width.Value;
			if(frameConstraints?.Height > 0)
				contentMeasureSize.Height = frameConstraints.Height.Value;

			if (Orientation == Orientation.Vertical)
				contentMeasureSize.Height = double.PositiveInfinity;
			else
				contentMeasureSize.Width = double.PositiveInfinity;
			
			if (Content is not null)
			{
				// Always remeasure content with current constraints — they change
				// on rotation and the old cached size would be stale.
				var contentSize = Content.Measure(contentMeasureSize.Width, contentMeasureSize.Height);
				Content.MeasuredSize = contentSize;
				MeasurementValid = true;
				return MeasuredSize = new Size(
					Math.Min(availableSize.Width, contentSize.Width),
					Math.Min(availableSize.Height, contentSize.Height));
				
			}
			if (frameConstraints?.Height > 0 && frameConstraints?.Width > 0)
				return MeasuredSize = new Size(frameConstraints.Width.Value, frameConstraints.Height.Value);
			return MeasuredSize = availableSize;
		}
		public override void LayoutSubviews(Rect frame)
		{
			this.Frame = frame;

			Content.SetFrameFromPlatformView(frame,LayoutAlignment.Start,	LayoutAlignment.Start);
			if (Content?.BuiltView is not null)
				Content.BuiltView.LayoutSubviews(frame);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				Content?.Dispose();
			base.Dispose(disposing);
		}

		void IScrollView.RequestScrollTo(double horizontalOffset, double verticalOffset, bool instant) { }
		void IScrollView.ScrollFinished() { }
	}
}
