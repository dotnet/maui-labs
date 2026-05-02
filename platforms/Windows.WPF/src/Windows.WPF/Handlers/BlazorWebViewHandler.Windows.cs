using System;
using System.IO;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;

namespace Microsoft.AspNetCore.Components.WebView.Maui.WPF
{
	/// <summary>
	/// A <see cref="ViewHandler"/> for <see cref="BlazorWebView"/>.
	/// </summary>
	public partial class BlazorWebViewHandler : WPFViewHandler<BlazorWebView, Wpf.BlazorWebView>
	{
		// Track the inner WebView2 reference so we can unsubscribe on disconnect even if
		// PlatformView.WebView has already been torn down.
		private WebView2Control? _hookedWebView;

		/// <inheritdoc />
		protected override Wpf.BlazorWebView CreatePlatformView()
		{
			return new Wpf.BlazorWebView();
		}

		public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			// Use explicit WidthRequest/HeightRequest if set, otherwise fill available space
			var w = (!double.IsInfinity(VirtualView.WidthRequest) && VirtualView.WidthRequest > 0)
				? VirtualView.WidthRequest : widthConstraint;
			var h = (!double.IsInfinity(VirtualView.HeightRequest) && VirtualView.HeightRequest > 0)
				? VirtualView.HeightRequest : heightConstraint;

			if (double.IsInfinity(w)) w = 800;
			if (double.IsInfinity(h)) h = 600;

			// PlatformView.WebView is lazily created after Loaded fires; guard against NRE during initial measure.
			PlatformView?.WebView?.Measure(new System.Windows.Size(w, h));
			return new Size(w, h);
		}

		public override void PlatformArrange(Rect rect)
		{
			base.PlatformArrange(rect);
			PlatformView?.WebView?.Arrange(new global::System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height));
		}

		protected override void ConnectHandler(Wpf.BlazorWebView platformView)
		{
			platformView.Loaded += PlatformView_Loaded;
			platformView.Services = MauiContext!.Services!;
			base.ConnectHandler(platformView);
		}

		private void WebView_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
		{
			PlatformView.Dispatcher.Invoke(() =>
			{
				PlatformView.InvalidateMeasure();
				PlatformView.InvalidateArrange();

				// Parent may not be a UIElement (e.g. logical-only parent during teardown). Soft-cast.
				if (PlatformView.Parent is System.Windows.UIElement parent)
				{
					parent.InvalidateMeasure();
					parent.InvalidateArrange();
				}
			});
		}

		void PlatformView_Loaded(object sender, System.Windows.RoutedEventArgs e)
		{
			// Track the inner WebView2 reference so DisconnectHandler can detach even if
			// PlatformView.WebView has been nulled out by the host control.
			_hookedWebView = PlatformView.WebView;
			if (_hookedWebView != null)
				_hookedWebView.NavigationCompleted += WebView_NavigationCompleted;

			StartWebViewCoreIfPossible();
		}

		/// <inheritdoc />
		protected override void DisconnectHandler(Wpf.BlazorWebView platformView)
		{
			platformView.Loaded -= PlatformView_Loaded;

			if (_hookedWebView != null)
			{
				try { _hookedWebView.NavigationCompleted -= WebView_NavigationCompleted; }
				catch { }
				_hookedWebView = null;
			}

			base.DisconnectHandler(platformView);
		}

		private bool RequiredStartupPropertiesSet =>
			HostPage != null &&
			Services != null;

		private void StartWebViewCoreIfPossible()
		{
			// Wpf.BlazorWebView owns its WebView2WebViewManager internally and starts it
			// once Services + HostPage + RootComponents have been mapped from the
			// virtual view. Nothing to do here — leaving the method as an explicit
			// extension point for future host-side initialization hooks.
		}

		internal IFileProvider CreateFileProvider(string contentRootDir)
		{
			// On WinUI we override HandleWebResourceRequest in WinUIWebViewManager so that loading static assets is done entirely there in an async manner.
			// This allows the code to be async because in WinUI all the file storage APIs are async-only, but IFileProvider is sync-only and we need to control
			// the precedence of which files are loaded from where.
			return new NullFileProvider();
		}
	}
}
