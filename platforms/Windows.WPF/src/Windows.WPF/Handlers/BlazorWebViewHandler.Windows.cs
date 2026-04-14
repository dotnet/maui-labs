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
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;

namespace Microsoft.AspNetCore.Components.WebView.Maui.WPF
{
	/// <summary>
	/// A <see cref="ViewHandler"/> for <see cref="BlazorWebView"/>.
	/// </summary>
	public partial class BlazorWebViewHandler : WPFViewHandler<BlazorWebView, Wpf.BlazorWebView>
	{
		//TODO
		//private WebView2WebViewManager? _webviewManager;

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

			PlatformView.WebView.Measure(new System.Windows.Size(w, h));
			return new Size(w, h);
		}

		public override void PlatformArrange(Rect rect)
		{
			base.PlatformArrange(rect);
			PlatformView.WebView.Arrange(new global::System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height));
		}

		protected override void ConnectHandler(Wpf.BlazorWebView platformView)
		{
			PlatformView.Loaded += PlatformView_Loaded;
			PlatformView.Services = MauiContext!.Services!;
			base.ConnectHandler(platformView);
		}

		private void WebView_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
		{
			PlatformView.Dispatcher.Invoke(() =>
			{
				PlatformView.InvalidateMeasure();
				PlatformView.InvalidateArrange();

				((System.Windows.UIElement)PlatformView.Parent).InvalidateMeasure();
				((System.Windows.UIElement)PlatformView.Parent).InvalidateArrange();
			});
		}

		void PlatformView_Loaded(object sender, System.Windows.RoutedEventArgs e)
		{
			PlatformView.WebView.NavigationCompleted += WebView_NavigationCompleted;
			//this.InvalidateMeasure(VirtualView);
			//PlatformView.Parent.InvalidateMeasure(VirtualView);
			StartWebViewCoreIfPossible();
		}

		/// <inheritdoc />
		protected override void DisconnectHandler(Wpf.BlazorWebView platformView)
		{
			//if (_webviewManager != null)
			//{
			//	// Dispose this component's contents and block on completion so that user-written disposal logic and
			//	// Blazor disposal logic will complete.
			//	_webviewManager?
			//		.DisposeAsync()
			//		.AsTask()
			//		.GetAwaiter()
			//		.GetResult();

			//	_webviewManager = null;
			//}
		}

		private bool RequiredStartupPropertiesSet =>
			//_webview != null &&
			HostPage != null &&
			Services != null;

		private void StartWebViewCoreIfPossible()
		{
			//if (PlatformView.WebView == null)
			//	return;

			//if (!RequiredStartupPropertiesSet ||
			//	_webviewManager != null)
			//{
			//	return;
			//}
			//if (PlatformView == null)
			//{
			//	throw new InvalidOperationException($"Can't start {nameof(BlazorWebView)} without native web view instance.");
			//}

			//var logger = Services!.GetService<ILogger<BlazorWebViewHandler>>() ?? NullLogger<BlazorWebViewHandler>.Instance;

			//// We assume the host page is always in the root of the content directory, because it's
			//// unclear there's any other use case. We can add more options later if so.
			//var contentRootDir = Path.GetDirectoryName(HostPage!) ?? string.Empty;
			//var hostPageRelativePath = Path.GetRelativePath(contentRootDir, HostPage!);

			//WebView.WPF.Log.CreatingFileProvider(logger, contentRootDir, hostPageRelativePath);
			//var fileProvider = VirtualView.CreateFileProvider(contentRootDir);

			//_webviewManager = new WebView2WebViewManager(
			//	PlatformView.WebView,
			//	Services!,
			//	new MauiDispatcher(Services!.GetRequiredService<IDispatcher>()),
			//	fileProvider,
			//	(VirtualView as IBlazorWebView).JSComponents,
			//	contentRootDir,
			//	hostPageRelativePath,
			//	(loading) =>
			//	{
			//	},
			//	(initializing) =>
			//	{
			//	},
			//	(initialized) =>
			//	{
			//	},
			//	logger);

			////	StaticContentHotReloadManager.AttachToWebViewManagerIfEnabled(_webviewManager);

			//if (RootComponents != null)
			//{
			//	foreach (var rootComponent in RootComponents)
			//	{
			//		if (rootComponent is null)
			//		{
			//			continue;
			//		}

			//		WebView.WPF.Log.AddingRootComponent(logger, rootComponent.ComponentType?.FullName ?? string.Empty, rootComponent.Selector ?? string.Empty, rootComponent.Parameters?.Count ?? 0);

			//		// Since the page isn't loaded yet, this will always complete synchronously
			//		_ = rootComponent.AddToWebViewManagerAsync(_webviewManager);
			//	}
			//}

			//WebView.WPF.Log.StartingInitialNavigation(logger, VirtualView.StartPath);
			//_webviewManager.Navigate(VirtualView.StartPath);
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
