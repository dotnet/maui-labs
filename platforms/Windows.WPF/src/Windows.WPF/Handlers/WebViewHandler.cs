using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Web.WebView2.Wpf;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class WebViewHandler : WPFViewHandler<Microsoft.Maui.Controls.WebView, WebView2>
	{
		public static readonly PropertyMapper<Microsoft.Maui.Controls.WebView, WebViewHandler> Mapper =
			new(ViewMapper)
			{
				[nameof(Microsoft.Maui.Controls.WebView.Source)] = MapSource,
				["UserAgent"] = MapUserAgent,
			};

		public static readonly CommandMapper<Microsoft.Maui.Controls.WebView, WebViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
				[nameof(Microsoft.Maui.Controls.WebView.GoBack)] = MapGoBack,
				[nameof(Microsoft.Maui.Controls.WebView.GoForward)] = MapGoForward,
				[nameof(Microsoft.Maui.Controls.WebView.Reload)] = MapReload,
				[nameof(Microsoft.Maui.Controls.WebView.Eval)] = MapEval,
				[nameof(Microsoft.Maui.Controls.WebView.EvaluateJavaScriptAsync)] = MapEvaluateJavaScriptAsync,
			};

		public WebViewHandler() : base(Mapper, CommandMapper) { }

		protected override WebView2 CreatePlatformView()
		{
			var wv = new WebView2();
			wv.NavigationStarting += OnNavigationStarting;
			wv.NavigationCompleted += OnNavigationCompleted;
			return wv;
		}

		void OnNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
		{
			// Navigation events handled via WebView2 directly
		}

		void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
		{
			if (VirtualView == null) return;
			try
			{
				// Update CanGoBack/CanGoForward
				var canGoBackProp = typeof(Microsoft.Maui.Controls.WebView).GetProperty("CanGoBack");
				var canGoForwardProp = typeof(Microsoft.Maui.Controls.WebView).GetProperty("CanGoForward");
				canGoBackProp?.SetValue(VirtualView, PlatformView.CanGoBack);
				canGoForwardProp?.SetValue(VirtualView, PlatformView.CanGoForward);
			}
			catch { }
		}

		static void MapSource(WebViewHandler handler, Microsoft.Maui.Controls.WebView view)
		{
			if (view.Source is Microsoft.Maui.Controls.UrlWebViewSource urlSource && !string.IsNullOrEmpty(urlSource.Url))
			{
				handler.PlatformView.Source = new Uri(urlSource.Url, UriKind.Absolute);
			}
			else if (view.Source is Microsoft.Maui.Controls.HtmlWebViewSource htmlSource && !string.IsNullOrEmpty(htmlSource.Html))
			{
				// Navigate to blank first, then inject HTML
				_ = handler.LoadHtmlAsync(htmlSource.Html);
			}
		}

		async Task LoadHtmlAsync(string html)
		{
			try
			{
				await PlatformView.EnsureCoreWebView2Async();
				PlatformView.NavigateToString(html);
			}
			catch { }
		}

		static void MapGoBack(WebViewHandler handler, Microsoft.Maui.Controls.WebView view, object? args)
		{
			if (handler.PlatformView.CanGoBack)
				handler.PlatformView.GoBack();
		}

		static void MapGoForward(WebViewHandler handler, Microsoft.Maui.Controls.WebView view, object? args)
		{
			if (handler.PlatformView.CanGoForward)
				handler.PlatformView.GoForward();
		}

		static void MapReload(WebViewHandler handler, Microsoft.Maui.Controls.WebView view, object? args)
		{
			handler.PlatformView.Reload();
		}

		static void MapEval(WebViewHandler handler, Microsoft.Maui.Controls.WebView view, object? args)
		{
			if (args is string js)
				_ = handler.EvalJsAsync(js);
		}

		static void MapEvaluateJavaScriptAsync(WebViewHandler handler, Microsoft.Maui.Controls.WebView view, object? args)
		{
			if (args is EvaluateJavaScriptAsyncRequest request)
			{
				_ = handler.EvalJsForRequestAsync(request);
			}
		}

		async Task EvalJsAsync(string js)
		{
			try
			{
				await PlatformView.EnsureCoreWebView2Async();
				await PlatformView.ExecuteScriptAsync(js);
			}
			catch { }
		}

		async Task EvalJsForRequestAsync(EvaluateJavaScriptAsyncRequest request)
		{
			try
			{
				await PlatformView.EnsureCoreWebView2Async();
				var result = await PlatformView.ExecuteScriptAsync(request.Script);
				request.SetResult(result);
			}
			catch (Exception)
			{
				request.SetResult(string.Empty);
			}
		}

		static void MapUserAgent(WebViewHandler handler, Microsoft.Maui.Controls.WebView view)
		{
			_ = handler.SetUserAgentAsync(view);
		}

		async Task SetUserAgentAsync(Microsoft.Maui.Controls.WebView view)
		{
			try
			{
				await PlatformView.EnsureCoreWebView2Async();
				var userAgent = view.GetType().GetProperty("UserAgent")?.GetValue(view) as string;
				if (!string.IsNullOrEmpty(userAgent))
					PlatformView.CoreWebView2.Settings.UserAgent = userAgent;
			}
			catch { }
		}

		protected override void DisconnectHandler(WebView2 platformView)
		{
			platformView.NavigationStarting -= OnNavigationStarting;
			platformView.NavigationCompleted -= OnNavigationCompleted;
			base.DisconnectHandler(platformView);
		}
	}
}
