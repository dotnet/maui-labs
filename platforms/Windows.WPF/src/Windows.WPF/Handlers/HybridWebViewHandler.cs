using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Maui.Handlers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class HybridWebViewHandler : WPFViewHandler<Microsoft.Maui.Controls.HybridWebView, WebView2>
	{
		private static readonly string AppOrigin = "https://0.0.0.1/";
		private static readonly Uri AppOriginUri = new(AppOrigin);

		public static readonly PropertyMapper<Microsoft.Maui.Controls.HybridWebView, HybridWebViewHandler> Mapper =
			new(ViewMapper)
			{
			};

		public static readonly CommandMapper<Microsoft.Maui.Controls.HybridWebView, HybridWebViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
			};

		public HybridWebViewHandler() : base(Mapper, CommandMapper) { }

		protected override WebView2 CreatePlatformView()
		{
			return new WebView2();
		}

		protected override void ConnectHandler(WebView2 platformView)
		{
			base.ConnectHandler(platformView);
			_ = InitializeWebViewAsync(platformView);
		}

		async Task InitializeWebViewAsync(WebView2 webView)
		{
			try
			{
				await webView.EnsureCoreWebView2Async();

				webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
				webView.CoreWebView2.AddWebResourceRequestedFilter($"{AppOrigin}*", CoreWebView2WebResourceContext.All);
				webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

				webView.CoreWebView2.Navigate(AppOrigin);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[HybridWebView] Init error: {ex.Message}");
			}
		}

		void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
		{
			try
			{
				// Only the page served from the app origin may talk to .NET.
				var fromAppOrigin = Uri.TryCreate(e.Source, UriKind.Absolute, out var source) && AppOriginUri.IsBaseOf(source);
				if (fromAppOrigin && VirtualView is IHybridWebView hybridWebView)
					HybridWebViewProtocol.HandleWebMessage(hybridWebView, e.TryGetWebMessageAsString());
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[HybridWebView] Message error: {ex.Message}");
			}
		}

		void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
		{
			try
			{
				var uri = new Uri(e.Request.Uri);
				if (!AppOriginUri.IsBaseOf(uri)) return;

				var relativePath = AppOriginUri.MakeRelativeUri(uri).ToString();
				relativePath = Uri.UnescapeDataString(relativePath);

				// Handle _framework/hybridwebview.js
				if (relativePath == "_framework/hybridwebview.js")
				{
					var jsStream = GetHybridWebViewScript();
					if (jsStream != null)
					{
						e.Response = PlatformView.CoreWebView2.Environment!.CreateWebResourceResponse(
							jsStream, 200, "OK", "Content-Type: application/javascript");
						return;
					}
				}

				// Handle __hwvInvokeDotNet (POST from JS)
				if (relativePath == HybridWebViewProtocol.InvokeDotNetPath)
				{
					_ = HandleInvokeDotNetRequestAsync(e);
					return;
				}

				// Serve local files from HybridRoot
				if (string.IsNullOrEmpty(relativePath))
					relativePath = VirtualView?.DefaultFile ?? "index.html";

				var hybridRoot = VirtualView?.HybridRoot ?? "";
				var assetPath = Path.Combine(AppContext.BaseDirectory, hybridRoot, relativePath);

				if (File.Exists(assetPath))
				{
					var contentType = GetContentType(relativePath);
					var stream = new MemoryStream(File.ReadAllBytes(assetPath));
					e.Response = PlatformView.CoreWebView2.Environment!.CreateWebResourceResponse(
						stream, 200, "OK", $"Content-Type: {contentType}");
				}
				else
				{
					e.Response = PlatformView.CoreWebView2.Environment!.CreateWebResourceResponse(
						null, 404, "Not Found", "");
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[HybridWebView] Resource error: {ex.Message}");
			}
		}

		async Task HandleInvokeDotNetRequestAsync(CoreWebView2WebResourceRequestedEventArgs e)
		{
			var environment = PlatformView.CoreWebView2.Environment!;
			var deferral = e.GetDeferral();
			try
			{
				var headers = e.Request.Headers;
				var hasToken = headers.Contains(HybridWebViewProtocol.InvokeTokenHeaderName) &&
					headers.GetHeader(HybridWebViewProtocol.InvokeTokenHeaderName) == HybridWebViewProtocol.InvokeTokenHeaderValue;
				var isPost = string.Equals(e.Request.Method, "POST", StringComparison.OrdinalIgnoreCase);
				if (!hasToken || !isPost || VirtualView is not IHybridWebView hybridWebView)
				{
					e.Response = environment.CreateWebResourceResponse(null, 400, "Bad Request", "");
					return;
				}

				var body = "";
				if (e.Request.Content is Stream requestStream)
				{
					using var reader = new StreamReader(requestStream);
					body = await reader.ReadToEndAsync();
				}

				// Also check header for body
				if (string.IsNullOrEmpty(body) && headers.Contains(HybridWebViewProtocol.RequestBodyHeaderName))
					body = headers.GetHeader(HybridWebViewProtocol.RequestBodyHeaderName) ?? "";

				if (string.IsNullOrEmpty(body))
				{
					e.Response = environment.CreateWebResourceResponse(null, 400, "Bad Request", "");
					return;
				}

				var responseBytes = await HybridWebViewProtocol.InvokeDotNetAsync(hybridWebView, body);
				e.Response = environment.CreateWebResourceResponse(
					new MemoryStream(responseBytes), 200, "OK", "Content-Type: application/json");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[HybridWebView] InvokeDotNet request error: {ex.Message}");
				e.Response = environment.CreateWebResourceResponse(null, 500, "Internal Server Error", "");
			}
			finally
			{
				deferral.Complete();
			}
		}

		Stream? GetHybridWebViewScript()
		{
			// The HybridWebView JS is embedded in Microsoft.Maui.dll
			var mauiAsm = typeof(Microsoft.Maui.IView).Assembly;
			var stream = mauiAsm.GetManifestResourceStream("_framework/hybridwebview.js");
			if (stream != null) return stream;

			// Try other common names
			foreach (var name in mauiAsm.GetManifestResourceNames())
			{
				if (name.Contains("hybridwebview", StringComparison.OrdinalIgnoreCase))
				{
					return mauiAsm.GetManifestResourceStream(name);
				}
			}

			// Fallback: minimal script for raw messages, in the same wire format as MAUI's script.
			var fallbackJs = @"
window.HybridWebView = window.HybridWebView || {
    SendRawMessage: function(message) {
        window.chrome.webview.postMessage(encodeURIComponent('__RawMessage|' + encodeURIComponent(message)));
    }
};
";
			return new MemoryStream(Encoding.UTF8.GetBytes(fallbackJs));
		}

		static string GetContentType(string path)
		{
			var ext = Path.GetExtension(path).ToLowerInvariant();
			return ext switch
			{
				".html" or ".htm" => "text/html",
				".js" => "application/javascript",
				".css" => "text/css",
				".json" => "application/json",
				".png" => "image/png",
				".jpg" or ".jpeg" => "image/jpeg",
				".gif" => "image/gif",
				".svg" => "image/svg+xml",
				".ico" => "image/x-icon",
				".woff" => "font/woff",
				".woff2" => "font/woff2",
				".ttf" => "font/ttf",
				".txt" => "text/plain",
				".xml" => "application/xml",
				".map" => "application/json",
				_ => "application/octet-stream",
			};
		}

		protected override void DisconnectHandler(WebView2 platformView)
		{
			if (platformView.CoreWebView2 != null)
			{
				platformView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
				platformView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
			}
			base.DisconnectHandler(platformView);
		}
	}
}
