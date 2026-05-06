using System;
using Microsoft.Maui;
using Microsoft.Maui.HotReload;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinSize = global::Windows.Foundation.Size;
using WinGrid = Microsoft.UI.Xaml.Controls.Grid;
using WinRect = global::Windows.Foundation.Rect;

namespace Comet.Windows
{
	public class CometView : WinGrid, IReloadHandler
	{
		IView _view;
		IViewHandler currentHandler;
		UIElement currentPlatformView;

		IMauiContext MauiContext;

		public CometView(IMauiContext mauiContext)
		{
			MauiContext = mauiContext;
			Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
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

			// Reuse handlers if view type is compatible
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

			// Resolve views with a Body (e.g. Component<T>) to their concrete view tree
			var viewToRender = _view;
			if (viewToRender is View cometView && cometView.Body is not null)
				viewToRender = cometView.GetView();
			var newPlatformView = viewToRender?.ToPlatform(MauiContext);
			currentHandler = _view?.Handler;

			if (currentPlatformView == newPlatformView)
				return;

			if (currentPlatformView is not null)
				Children.Remove(currentPlatformView);

			if (newPlatformView != this && newPlatformView is not null)
			{
				currentPlatformView = newPlatformView;
				Children.Add(currentPlatformView);
			}
		}

		protected override WinSize MeasureOverride(WinSize availableSize)
		{
			if (_view is null)
				return availableSize;

			var width = availableSize.Width > 0 ? availableSize.Width : 1000;
			var height = availableSize.Height > 0 ? availableSize.Height : 1000;

			var size = _view.Measure(width, height);
			return new WinSize(size.Width, size.Height);
		}

		protected override WinSize ArrangeOverride(WinSize finalSize)
		{
			if (_view is null)
				return finalSize;

			_view.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));

			if (currentPlatformView is not null)
			{
				currentPlatformView.Arrange(new WinRect(0, 0, finalSize.Width, finalSize.Height));
			}

			return finalSize;
		}

		public void Reload() => SetView(CurrentView, true);
	}
}
