using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
				var rawMessage = e.TryGetWebMessageAsString();
				if (string.IsNullOrEmpty(rawMessage)) return;

				// HybridWebView messages: "N|methodName|callbackId|jsonArgs"
				var pipeIndex = rawMessage.IndexOf('|');
				if (pipeIndex == -1) return;

				var messageType = rawMessage.Substring(0, pipeIndex);
				var messageContent = rawMessage.Substring(pipeIndex + 1);

				if (messageType == "__InvokeDotNet")
				{
					HandleInvokeDotNet(messageContent);
				}
				else if (messageType == "__RawMessage")
				{
					// Fire RawMessageReceived on the virtual view
					var rmrMethod = VirtualView?.GetType().GetMethod("OnRawMessageReceived",
						BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					rmrMethod?.Invoke(VirtualView, new object[] { messageContent });
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[HybridWebView] Message error: {ex.Message}");
			}
		}

		void HandleInvokeDotNet(string content)
		{
			try
			{
				// Get the invoke target from the virtual view
				var targetProp = VirtualView?.GetType().GetProperty("JSInvokeTarget",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				var target = targetProp?.GetValue(VirtualView);
				if (target == null) return;

				// Parse: methodName|callbackId|jsonArgs
				var parts = content.Split(new[] { '|' }, 3);
				if (parts.Length < 2) return;

				var methodName = parts[0];
				var callbackId = parts[1];
				var jsonArgs = parts.Length > 2 ? parts[2] : "[]";

				var method = target.GetType().GetMethod(methodName,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
				if (method == null) return;

				var parameters = method.GetParameters();
				var args = new object?[parameters.Length];

				if (!string.IsNullOrEmpty(jsonArgs) && jsonArgs != "[]")
				{
					using var doc = JsonDocument.Parse(jsonArgs);
					var arr = doc.RootElement;
					for (int i = 0; i < parameters.Length && i < arr.GetArrayLength(); i++)
					{
						args[i] = JsonSerializer.Deserialize(arr[i].GetRawText(), parameters[i].ParameterType);
					}
				}

				var result = method.Invoke(target, args);

				if (!string.IsNullOrEmpty(callbackId))
				{
					var resultJson = result != null ? JsonSerializer.Serialize(result) : "null";
					var js = $"window.__HybridWebView__DispatchResult('{callbackId}', {resultJson})";
					_ = PlatformView.ExecuteScriptAsync(js);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[HybridWebView] InvokeDotNet error: {ex.Message}");
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
				if (relativePath == "__hwvInvokeDotNet")
				{
					HandleInvokeDotNetRequest(e);
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

		void HandleInvokeDotNetRequest(CoreWebView2WebResourceRequestedEventArgs e)
		{
			try
			{
				var body = "";
				if (e.Request.Content is Stream requestStream)
				{
					using var reader = new StreamReader(requestStream);
					body = reader.ReadToEnd();
				}

				// Also check header for body
				if (string.IsNullOrEmpty(body))
				{
					body = e.Request.Headers.GetHeader("X-Maui-Request-Body") ?? "";
				}

				if (!string.IsNullOrEmpty(body))
				{
					HandleInvokeDotNet(body);
				}

				var responseBytes = Encoding.UTF8.GetBytes("{}");
				var responseStream = new MemoryStream(responseBytes);
				e.Response = PlatformView.CoreWebView2.Environment!.CreateWebResourceResponse(
					responseStream, 200, "OK", "Content-Type: application/json");
			}
			catch
			{
				e.Response = PlatformView.CoreWebView2.Environment!.CreateWebResourceResponse(
					null, 500, "Internal Server Error", "");
			}
		}

		Stream? GetHybridWebViewScript()
		{
			// The HybridWebView JS is embedded in Microsoft.Maui.dll
			var mauiAsm = typeof(Microsoft.Maui.IView).Assembly;
			var stream = mauiAsm.GetManifestResourceStream("Microsoft.Maui.Platform.HybridWebView.hybridwebview.js");
			if (stream != null) return stream;

			// Try other common names
			foreach (var name in mauiAsm.GetManifestResourceNames())
			{
				if (name.Contains("hybridwebview", StringComparison.OrdinalIgnoreCase))
				{
					return mauiAsm.GetManifestResourceStream(name);
				}
			}

			// Fallback: minimal script that enables basic JS→.NET communication
			var fallbackJs = @"
window.__HybridWebView = {
    SendInvokeMessageToDotNet: function(methodName, paramValues) {
        var msg = '__InvokeDotNet|' + methodName + '||' + JSON.stringify(paramValues || []);
        window.chrome.webview.postMessage(msg);
    },
    SendRawMessage: function(message) {
        window.chrome.webview.postMessage('__RawMessage|' + message);
    }
};
window.__HybridWebView__DispatchResult = function(callbackId, result) {};
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
