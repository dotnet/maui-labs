using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using AScrollView = Android.Widget.ScrollView;
using AView = Android.Views.View;
using AViewGroup = Android.Views.ViewGroup;
using HScrollView = Android.Widget.HorizontalScrollView;

namespace Comet.Android.Controls
{
	public class CometScrollView : AScrollView
	{
		AViewGroup scrollView;
		AView currentView;
		Orientation currentOrientation;
		public void SetVirtualView(ScrollView view, IMauiContext context)
		{
			var newContent = view.Content?.ToPlatform(context);
			if(scrollView is null || currentOrientation != view.Orientation)
			{
				if (currentView is not null)
					scrollView?.RemoveView(currentView);
				currentView = null;
				if (scrollView is not null)
					this.RemoveView(scrollView);

				currentOrientation = view.Orientation;
				AddView(scrollView = currentOrientation == Orientation.Horizontal ? new HScrollView(Context) : new AScrollView(Context));
			}

			if (newContent != currentView) {
				if(currentView is not null)
					scrollView?.RemoveView(currentView);
				currentView = newContent;
				scrollView.AddView(currentView);
			}
		
		}
		public CometScrollView(Context context) : base(context)
		{
		}

		internal Action<Rect> CrossPlatformArrange { get; set; }

		protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
		{
			scrollView?.Layout(0, 0, this.Width, this.Height);
			if (CrossPlatformArrange is null || Context is null)
			{
				return;
			}
			var deviceIndependentLeft = Context.FromPixels(left);
			var deviceIndependentTop = Context.FromPixels(top);
			var deviceIndependentRight = Context.FromPixels(right);
			var deviceIndependentBottom = Context.FromPixels(bottom);

			var destination = Rect.FromLTRB(0, 0,
				deviceIndependentRight - deviceIndependentLeft, deviceIndependentBottom - deviceIndependentTop);

			CrossPlatformArrange(destination);
		}
	}
}
