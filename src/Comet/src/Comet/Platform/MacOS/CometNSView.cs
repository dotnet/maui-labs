using System;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.HotReload;
namespace Comet.MacOS
{
	public class CometNSView : NSView, IReloadHandler
	{
		public CometNSView(IMauiContext mauiContext)
		{
			MauiContext = mauiContext;
			WantsLayer = true;
		}

		IView _view;
		public IView CurrentView
		{
			get => _view;
			set => SetView(value);
		}

		public IMauiContext MauiContext { get; internal set; }

		NSView currentPlatformView;
		IViewHandler currentHandler;

		void SetView(IView view, bool forceRefresh = false)
		{
			if (view == _view && !forceRefresh)
				return;

			// Reuse handlers when content type matches
			if (view is View v && _view is View pv &&
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
				return;
			}

			_view = view;

			if (_view is IHotReloadableView ihr)
			{
				ihr.ReloadHandler = this;
				MauiHotReloadHelper.AddActiveView(ihr);
			}

			// Resolve views with a Body to their concrete tree to avoid
			// circular CometViewHandler→CometNSView loops
			var viewToRender = _view;
			if (viewToRender is View cometView && cometView.Body is not null)
				viewToRender = cometView.GetView();

			var newPlatformView = viewToRender?.ToMacOSPlatform(MauiContext);
			currentHandler = _view?.Handler;
			if (currentPlatformView == newPlatformView)
				return;

			currentPlatformView?.RemoveFromSuperview();
			if (newPlatformView != this && newPlatformView is not null)
			{
				AddSubview(newPlatformView);
				currentPlatformView = newPlatformView;
			}
		}

		public override void Layout()
		{
			base.Layout();
			if (currentPlatformView is null)
				return;
			_view?.Measure(Bounds.Width, Bounds.Height);
			currentPlatformView.Frame = Bounds;
		}

		public void UpdateBackground(IView view)
		{
			WantsLayer = true;
			if (Layer is null)
				return;

			if (view.Background is Microsoft.Maui.Graphics.SolidPaint solid && solid.Color is not null)
			{
				var color = solid.Color;
				Layer.BackgroundColor = CoreGraphics.CGColor.CreateSrgb(
					(nfloat)color.Red, (nfloat)color.Green, (nfloat)color.Blue, (nfloat)color.Alpha);
			}
		}

		public void Reload() => SetView(CurrentView, true);
	}
}
