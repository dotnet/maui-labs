using System;
using AContext = Android.Content.Context;
using AView = Android.Views.View;
using AViewGroup = Android.Views.ViewGroup;
using Microsoft.Maui;
using Microsoft.Maui.HotReload;
using Microsoft.Maui.Graphics;

namespace Comet.Android
{
	public class CometView : AViewGroup, IReloadHandler
	{
		IView _view;
		IViewHandler currentHandler;
		AView currentPlatformView;
		private bool inLayout;

		IMauiContext MauiContext;
		public CometView(IMauiContext mc) : base(mc.Context)
		{
			MauiContext = mc;
		}

		public IView CurrentView
		{
			get => _view;
			set => SetView(value);
		}

		void SetView(IView view, bool forceRefresh = false)
		{
			if (view == _view && !forceRefresh)
				return;

			_view = view;


			if (_view is IHotReloadableView ihr)
			{
				ihr.ReloadHandler = this;
				MauiHotReloadHelper.AddActiveView(ihr);
			}
			// Resolve views with a Body (e.g. Component<T>) to their concrete view tree
			var viewToRender = _view;
			if (viewToRender is View cometView && cometView.Body is not null)
				viewToRender = cometView.GetView();
			var newPlatformView = viewToRender?.ToPlatform(MauiContext);

			if (view is IReplaceableView ir)
				currentHandler = ir.ReplacedView.Handler;
			else
				currentHandler = _view?.Handler;
			if (currentPlatformView == newPlatformView)
				return;
			if (currentPlatformView is not null)
				RemoveView(currentPlatformView);
			if (_view is null)
				return;

			currentPlatformView = currentHandler.PlatformView as AView ?? new AView(MauiContext.Context);
			if (currentPlatformView.Parent == this)
				return;
			if (currentPlatformView.Parent is not null)
				(currentPlatformView.Parent as AViewGroup).RemoveView(currentPlatformView);
			AddView(currentPlatformView, new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));

		}

		private void HandleNeedsLayout(object sender, EventArgs e)
		{
			RequestLayout();
		}

		protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
		{
			var widthMode = AView.MeasureSpec.GetMode(widthMeasureSpec);
			var heightMode = AView.MeasureSpec.GetMode(heightMeasureSpec);
			var widthSize = AView.MeasureSpec.GetSize(widthMeasureSpec);
			var heightSize = AView.MeasureSpec.GetSize(heightMeasureSpec);

			double nativeWidth = widthSize;
			double nativeHeight = heightSize;

			if (CurrentView is not null)
			{
				var deviceIndependentWidth = widthMeasureSpec.ToDouble(Context);
				var deviceIndependentHeight = heightMeasureSpec.ToDouble(Context);
				var size = CurrentView.Measure(deviceIndependentWidth, deviceIndependentHeight);
				nativeWidth = Context.ToPixels(size.Width);
				nativeHeight = Context.ToPixels(size.Height);
			}

			// Respect EXACTLY/AT_MOST constraints from parent
			if (widthMode == global::Android.Views.MeasureSpecMode.Exactly)
				nativeWidth = widthSize;
			else if (widthMode == global::Android.Views.MeasureSpecMode.AtMost)
				nativeWidth = Math.Min(nativeWidth, widthSize);

			if (heightMode == global::Android.Views.MeasureSpecMode.Exactly)
				nativeHeight = heightSize;
			else if (heightMode == global::Android.Views.MeasureSpecMode.AtMost)
				nativeHeight = Math.Min(nativeHeight, heightSize);

			// Measure children with our resolved size
			for (int i = 0; i < ChildCount; i++)
			{
				var child = GetChildAt(i);
				child?.Measure(
					AView.MeasureSpec.MakeMeasureSpec((int)nativeWidth, global::Android.Views.MeasureSpecMode.Exactly),
					AView.MeasureSpec.MakeMeasureSpec((int)nativeHeight, global::Android.Views.MeasureSpecMode.Exactly));
			}

			SetMeasuredDimension((int)nativeWidth, (int)nativeHeight);
		}

		protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
		{
			if (currentPlatformView is null || inLayout) return;

			var displayScale = Context.Resources.DisplayMetrics.Density;
			var width = (right - left) / displayScale;
			var height = (bottom - top) / displayScale;
			if (width > 0 && height > 0)
			{
				inLayout = true;
				var rect = new Rect(0, 0, width, height);
				CurrentView.Arrange(rect); 
				inLayout = false;
			}
		}
		protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
		{
			base.OnSizeChanged(w, h, oldw, oldh);
			var displayScale = Context.Resources.DisplayMetrics.Density;
			var width = w / displayScale;
			var height = h / displayScale;
			if (width > 0 && height > 0)
			{
				inLayout = true;
				var rect = new Rect(0, 0, width, height);
				CurrentView.Arrange(rect);
				inLayout = false;
			}
		}

		protected override void Dispose(bool disposing)
		{
			//if (disposing)
			//	CurrentView?.Dispose();
			base.Dispose(disposing);
		}
		public void Reload() => SetView(CurrentView, true);
	}
}