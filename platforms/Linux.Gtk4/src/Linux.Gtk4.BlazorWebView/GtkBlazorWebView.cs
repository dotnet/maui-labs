using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.FileProviders;
using WebKit;
using WebView = WebKit.WebView;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;

/// <summary>
/// WebKitGTK WebView wrapper for Blazor Hybrid on Linux.
/// Registers custom app:// URI scheme and handles JS interop.
/// Based on the DevToys.Linux BlazorWebView implementation.
/// </summary>
public sealed class GtkBlazorWebView : IDisposable
{
	private const string InteropName = "mauilinuxinterop";
	private const string Scheme = "app";
	internal const string AppHostAddress = "localhost";
	internal static readonly Uri AppOriginUri = new($"{Scheme}://{AppHostAddress}/");

	private const string BlazorInitScript =
		$$"""
		window.__receiveMessageCallbacks = [];
		window.__dispatchMessageCallback = function(message) {
			window.__receiveMessageCallbacks.forEach(
				function(callback) {
					try { callback(message); } catch { }
				});
		};
		window.external = {
			sendMessage: function(message) {
				window.webkit.messageHandlers.{{InteropName}}.postMessage(message);
			},
			receiveMessage: function(callback) {
				window.__receiveMessageCallbacks.push(callback);
			}
		};
		try { Blazor.start(); } catch {}
		(function () {
			window.onpageshow = function(event) {
				if (event.persisted) { window.location.reload(); }
			};
		})();
		""";

	private readonly IServiceProvider _serviceProvider;
	private GtkWebViewManager? _webViewManager;
	private string? _hostPage;
	private static int _moduleInitialized;

	/// <summary>
	/// Ensures WebKitGTK native libraries are properly resolved and the sandbox
	/// is disabled for environments (VMs, containers) where bubblewrap fails.
	/// Call this early in Program.Main before any WebKit types are used.
	/// </summary>
	public static void InitializeWebKit()
	{
		if (Interlocked.Exchange(ref _moduleInitialized, 1) != 0) return;

		WebKitSandboxHelper.ConfigureSandbox();

		// Register GirCore DLL import resolver so "WebKit" maps to libwebkitgtk-6.0.so
		WebKit.Module.Initialize();
	}

	public GtkBlazorWebView(IServiceProvider serviceProvider)
	{
		InitializeWebKit();
		_serviceProvider = serviceProvider;
		WebKitWebView = CreateWebView();
		RootComponents = new RootComponentsCollection();
	}

	internal WebView WebKitWebView { get; }

	/// <summary>
	/// Gets the GTK widget for embedding in the UI tree.
	/// </summary>
	public Gtk.Widget Widget => WebKitWebView;

	public string? HostPage
	{
		get => _hostPage;
		set
		{
			_hostPage = value;
			StartWebViewCoreIfPossible();
		}
	}

	public string StartPath { get; set; } = "/";

	public RootComponentsCollection RootComponents { get; }

	public void Dispose()
	{
		if (_webViewManager != null)
		{
			// Offload blocking wait to thread pool to avoid deadlocking the GTK main thread
			var manager = _webViewManager;
			_webViewManager = null;
			Task.Run(async () =>
			{
				try { await manager.DisposeAsync(); }
				catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"GtkBlazorWebView: DisposeAsync failed: {ex.Message}"); }
			}).Wait(TimeSpan.FromSeconds(5));
		}

		WebKitWebView.Dispose();
	}

	private WebView CreateWebView()
	{
		var webView = new WebView();

		// Make web view transparent for Blazor styling
		webView.SetBackgroundColor(new Gdk.RGBA { Red = 0, Blue = 0, Green = 0, Alpha = 0 });

		// Configure settings
		var settings = webView.GetSettings();
		settings.JavascriptCanAccessClipboard = true;
		settings.EnableBackForwardNavigationGestures = false;
		webView.SetSettings(settings);

		// Handle JS→.NET messages
		var userContentManager = webView.GetUserContentManager();
		UserContentManager.ScriptMessageReceivedSignal.Connect(
			userContentManager,
			HandleScriptMessage,
			after: false,
			detail: InteropName);
		userContentManager.RegisterScriptMessageHandler(InteropName, null);

		// Inject Blazor initialization script
		userContentManager.AddScript(
			UserScript.New(
				BlazorInitScript,
				injectedFrames: UserContentInjectedFrames.AllFrames,
				injectionTime: UserScriptInjectionTime.End,
				allowList: null,
				blockList: null));

		// Register app:// scheme for Blazor static content
		webView.WebContext?.RegisterUriScheme(Scheme, HandleUriScheme);

		return webView;
	}

	private void HandleScriptMessage(
		UserContentManager ucm,
		UserContentManager.ScriptMessageReceivedSignalArgs args)
	{
		var result = args.Value;
		_webViewManager?.MessageReceivedInternal(AppOriginUri, result.ToString());
	}

	private void HandleUriScheme(URISchemeRequest request)
	{
		if (request.GetScheme() != Scheme)
		{
			FinishUriSchemeRequest(request, 400, "Bad Request", "Unsupported URI scheme.");
			return;
		}

		if (_webViewManager == null)
		{
			FinishUriSchemeRequest(request, 503, "Service Unavailable", "Blazor WebView is not ready yet.");
			return;
		}

		string uri = request.GetUri();
		bool allowFallbackOnHostPage = !System.IO.Path.HasExtension(uri);

		if (_webViewManager.TryGetResponseContentInternal(
			uri,
			allowFallbackOnHostPage,
			out int statusCode,
			out string statusMessage,
			out Stream content,
			out IDictionary<string, string> headers))
		{
			using var ms = new MemoryStream();
			content.CopyTo(ms);
			content.Dispose();

			var contentType = headers.TryGetValue("Content-Type", out var ct) ? ct : "application/octet-stream";
			using var bytes = GLib.Bytes.New(ms.GetBuffer().AsSpan(0, (int)ms.Length));
			using var inputStream = Gio.MemoryInputStream.NewFromBytes(bytes);

			using var response = URISchemeResponse.New(inputStream, ms.Length);
			response.SetContentType(contentType);
			response.SetStatus((uint)statusCode, statusMessage);
			request.FinishWithResponse(response);
			return;
		}

		FinishUriSchemeRequest(request, 404, "Not Found", $"No Blazor content was found for '{uri}'.");
	}

	private void StartWebViewCoreIfPossible()
	{
		if (HostPage == null || _webViewManager != null)
			return;

		string contentRootDir = System.IO.Path.GetDirectoryName(HostPage!) ?? string.Empty;
		string hostPageRelativePath = System.IO.Path.GetRelativePath(contentRootDir, HostPage!);

		var fileProvider = CreateFileProvider(contentRootDir);

		_webViewManager = new GtkWebViewManager(
			AppOriginUri,
			this,
			_serviceProvider,
			fileProvider,
			RootComponents.JSComponents,
			contentRootDir,
			hostPageRelativePath);

		foreach (var rootComponent in RootComponents)
		{
			_ = rootComponent.AddToWebViewManagerAsync(_webViewManager);
		}

		_webViewManager.Navigate(StartPath);
	}

	private static IFileProvider CreateFileProvider(string contentRootDir)
	{
		string contentRoot = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!;
		string bundleRootDir = System.IO.Path.Combine(contentRoot, contentRootDir);

		if (Directory.Exists(bundleRootDir))
			return new PhysicalFileProvider(bundleRootDir);

		return new NullFileProvider();
	}

	private static void FinishUriSchemeRequest(URISchemeRequest request, uint statusCode, string statusText, string message)
	{
		var body = Encoding.UTF8.GetBytes($"<html><body><h1>{statusCode} {statusText}</h1><p>{message}</p></body></html>");
		using var bytes = GLib.Bytes.New(body);
		using var stream = Gio.MemoryInputStream.NewFromBytes(bytes);
		using var response = URISchemeResponse.New(stream, body.Length);
		response.SetContentType("text/html; charset=utf-8");
		response.SetStatus(statusCode, statusText);
		request.FinishWithResponse(response);
	}
}
