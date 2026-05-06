using System;
using System.Windows;
using Microsoft.Maui.Platform;
using WScrollViewer = System.Windows.Controls.ScrollViewer;
using WScrollChangedEventArgs = System.Windows.Controls.ScrollChangedEventArgs;
using WScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ScrollViewHandler : WPFViewHandler<IScrollView, WScrollViewer>
	{
		protected override WScrollViewer CreatePlatformView()
		{
			return new WScrollViewer
			{
				HorizontalScrollBarVisibility = WScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = WScrollBarVisibility.Auto,
			};
		}

		protected override void ConnectHandler(WScrollViewer platformView)
		{
			base.ConnectHandler(platformView);
			platformView.ScrollChanged += OnScrollChanged;
		}

		protected override void DisconnectHandler(WScrollViewer platformView)
		{
			platformView.ScrollChanged -= OnScrollChanged;
			base.DisconnectHandler(platformView);
		}

		void OnScrollChanged(object sender, WScrollChangedEventArgs e)
		{
			VirtualView?.ScrollFinished();
		}

		public static void MapContent(ScrollViewHandler handler, IScrollView scrollView)
		{
			if (handler.MauiContext == null)
				return;

			if (scrollView.PresentedContent is IView view)
			{
				handler.PlatformView.Content = view.ToPlatform(handler.MauiContext);
			}
		}

		public static void MapHorizontalScrollBarVisibility(ScrollViewHandler handler, IScrollView scrollView)
		{
			handler.PlatformView.HorizontalScrollBarVisibility = scrollView.HorizontalScrollBarVisibility switch
			{
				ScrollBarVisibility.Always => WScrollBarVisibility.Visible,
				ScrollBarVisibility.Never => WScrollBarVisibility.Hidden,
				ScrollBarVisibility.Default => WScrollBarVisibility.Auto,
				_ => WScrollBarVisibility.Disabled,
			};
		}

		public static void MapVerticalScrollBarVisibility(ScrollViewHandler handler, IScrollView scrollView)
		{
			handler.PlatformView.VerticalScrollBarVisibility = scrollView.VerticalScrollBarVisibility switch
			{
				ScrollBarVisibility.Always => WScrollBarVisibility.Visible,
				ScrollBarVisibility.Never => WScrollBarVisibility.Hidden,
				ScrollBarVisibility.Default => WScrollBarVisibility.Auto,
				_ => WScrollBarVisibility.Auto,
			};
		}

		public static void MapOrientation(ScrollViewHandler handler, IScrollView scrollView)
		{
			handler.PlatformView.HorizontalScrollBarVisibility = scrollView.Orientation == ScrollOrientation.Horizontal || scrollView.Orientation == ScrollOrientation.Both
				? WScrollBarVisibility.Auto
				: WScrollBarVisibility.Disabled;
		}

		public static void MapRequestScrollTo(ScrollViewHandler handler, IScrollView scrollView, object? args)
		{
			if (args is ScrollToRequest request)
			{
				handler.PlatformView.ScrollToHorizontalOffset(request.HorizontalOffset);
				handler.PlatformView.ScrollToVerticalOffset(request.VerticalOffset);
				scrollView.ScrollFinished();
			}
		}
	}
}
