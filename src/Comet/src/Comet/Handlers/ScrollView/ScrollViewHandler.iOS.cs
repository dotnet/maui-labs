using System;
using Foundation;
using UIKit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui;
using Comet.iOS;
using Microsoft.Maui.Graphics;
using CoreGraphics;

namespace Comet.Handlers
{
	public partial class ScrollViewHandler : ViewHandler<ScrollView, CUIScrollView>
	{

		private UIView _content;

		protected override CUIScrollView CreatePlatformView() =>
			new CUIScrollView {
				ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Automatic,
				CrossPlatformArrange = Arange,
			};

		void Arange(Rect rect)
		{
			var isVertical = this.VirtualView.Orientation == Orientation.Vertical;
			var sizeAllowed = isVertical
				? new Size(rect.Width, Double.PositiveInfinity)
				: new Size(Double.PositiveInfinity, rect.Height);

			// Invalidate content measurement so it picks up the new width/height
			// constraint (e.g. after rotation).
			if (VirtualView?.Content is View contentView)
				contentView.MeasurementValid = false;

			var measuredSize = VirtualView?.Content?.Measure(sizeAllowed.Width, sizeAllowed.Height) ?? Size.Zero;

			if (double.IsInfinity(measuredSize.Width))
				measuredSize.Width = rect.Width;
			if (double.IsInfinity(measuredSize.Height))
				measuredSize.Height = rect.Height;

			// Clamp the non-scrolling dimension to the scroll view's bounds
			// so a vertical ScrollView never scrolls horizontally and vice-versa.
			if (isVertical)
			{
				measuredSize.Width = rect.Width;
				measuredSize.Height = Math.Max(measuredSize.Height, rect.Height);
			}
			else
			{
				measuredSize.Width = Math.Max(measuredSize.Width, rect.Width);
				measuredSize.Height = rect.Height;
			}

			PlatformView.ContentSize = measuredSize.ToCGSize();
			_content.Frame = new CGRect(CGPoint.Empty, measuredSize);
		}

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);

			// ScrollView content must ignore safe area — the ScrollView itself
			// handles safe area via ContentInsetAdjustmentBehavior. Without this,
			// MAUI's LayoutView (MauiView) will double-apply safe area insets to
			// the content's CrossPlatformArrange, pushing it below the nav bar.
			if (VirtualView?.Content is View contentView)
				contentView.SetEnvironment(EnvironmentKeys.Layout.IgnoreSafeArea, true, false);

			var oldContent = _content;
			_content = VirtualView?.Content?.ToPlatform(MauiContext);
			if(oldContent != _content)
				oldContent?.RemoveFromSuperview();
			if (_content is not null)
			{
				PlatformView.Add(_content);
			}

			if (VirtualView.Orientation == Orientation.Horizontal)
			{
				PlatformView.ShowsVerticalScrollIndicator = false;
				PlatformView.ShowsHorizontalScrollIndicator = true;
			}
			else
			{
				PlatformView.ShowsVerticalScrollIndicator = true;
				PlatformView.ShowsHorizontalScrollIndicator = false;
			}
		}

	}
}
