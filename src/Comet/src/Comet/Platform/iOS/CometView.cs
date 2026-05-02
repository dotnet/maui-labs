using System;
using CoreGraphics;
using UIKit;
using Microsoft.Maui.HotReload;
using Microsoft.Maui;

namespace Comet.iOS
{
	public class CometView : UIView, IReloadHandler
	{
		bool _inLayout;

		public CometView(IMauiContext mauiContext) {
			MauiContext = mauiContext;
			BackgroundColor = UIColor.SystemBackground;
		}
		public CometView(CGRect rect, IMauiContext mauiContext) : base(rect)
		{
			MauiContext = mauiContext;
			BackgroundColor = UIColor.SystemBackground;
		}
		IView _view;
		public IView CurrentView
		{
			get => _view;
			set => SetView(value);
		}
		public IMauiContext MauiContext { get; internal set; }

		UIView currentPlatformView;
		IViewHandler currentHandler;
		void SetView(IView view, bool forceRefresh = false)
		{
			if (view == _view && !forceRefresh)
				return;
			//reuse the handlers!
			if(view is View v && _view is View pv &&
				v.GetContentTypeHashCode() == pv.GetContentTypeHashCode()
				&& currentHandler is not null)
			{
				_view = view;
				v.ViewHandler = currentHandler;
				if (_view is IHotReloadableView ihr1)
				{
					ihr1.ReloadHandler = this;
					MauiHotReloadHelper.AddActiveView(ihr1);
				}
				PropagateContentBackground();
				return;
			}

			_view = view;

			if (_view is IHotReloadableView ihr)
			{
				ihr.ReloadHandler = this;
				MauiHotReloadHelper.AddActiveView(ihr);
			}
			// Resolve views with a Body (e.g. Component<T>) to their concrete view tree
			// before calling ToPlatform, to avoid circular CometViewHandler→CometView loop.
			var viewToRender = _view;
			if (viewToRender is View cometView && cometView.Body is not null)
			{
				viewToRender = cometView.GetView();
			}
			var newPlatformView = viewToRender?.ToPlatform(MauiContext);
			currentHandler = _view?.Handler;
			if (currentPlatformView == newPlatformView)
				return;
			currentPlatformView?.RemoveFromSuperview();
			if (newPlatformView != this && newPlatformView is not null)
				AddSubview(currentPlatformView = newPlatformView);

			PropagateContentBackground();
		}

		/// <summary>
		/// Copies the content view's background to this container so the safe area
		/// regions (behind status bar, home indicator) show the correct color
		/// instead of the default SystemBackground.
		/// </summary>
		void PropagateContentBackground()
		{
			if (_view is not View cometView) return;

			var bg = cometView.GetBackground();
			// Walk into the rendered body if the component itself has no background
			if (bg is null && cometView.Body is not null)
			{
				var bodyView = cometView.GetView() as View;
				bg = bodyView?.GetBackground();
			}

			if (bg is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color is not null)
				BackgroundColor = solid.Color.ToPlatform();
		}


		public override void LayoutSubviews()
		{
			if (_inLayout)
				return;
			_inLayout = true;
			try
			{
				base.LayoutSubviews();
				if (currentPlatformView is null || Bounds.Width <= 0 || Bounds.Height <= 0)
					return;

				// Invalidate measurement so the view tree remeasures with
				// new constraints (critical for device rotation).
				if (_view is View cometView)
					cometView.MeasurementValid = false;

				_view?.Measure(Bounds.Width, Bounds.Height);
				_view?.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, Bounds.Width, Bounds.Height));
				currentPlatformView.Frame = Bounds;
				currentPlatformView.SetNeedsLayout();
				currentPlatformView.LayoutIfNeeded();
			}
			finally
			{
				_inLayout = false;
			}
		}



		public void Reload() => SetView(CurrentView, true);
	}
}
