using System.Runtime.InteropServices;
using Microsoft.Maui;
using WebKit;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class WebViewHandler : GtkViewHandler<IWebView, WebKit.WebView>, IWebViewDelegate
{
	static bool _moduleInitialized;
	WebNavigationEvent _currentNavigationEvent = WebNavigationEvent.NewPage;
	string _lastUrl = "about:blank";

	[DllImport("libc", SetLastError = true)]
	static extern int setenv(string name, string value, int overwrite);

	public static new IPropertyMapper<IWebView, WebViewHandler> Mapper =
		new PropertyMapper<IWebView, WebViewHandler>(ViewMapper)
		{
			[nameof(IWebView.Source)] = MapSource,
			[nameof(IWebView.UserAgent)] = MapUserAgent,
		};

	public static CommandMapper<IWebView, WebViewHandler> CommandMapper = new(ViewCommandMapper)
	{
		[nameof(IWebView.GoBack)] = MapGoBack,
		[nameof(IWebView.GoForward)] = MapGoForward,
		[nameof(IWebView.Reload)] = MapReload,
		[nameof(IWebView.Eval)] = MapEval,
		[nameof(IWebView.EvaluateJavaScriptAsync)] = MapEvaluateJavaScriptAsync,
	};

	public WebViewHandler() : base(Mapper, CommandMapper)
	{
	}

	protected override WebKit.WebView CreatePlatformView()
	{
		EnsureWebKitInitialized();
		var webView = new WebKit.WebView();

		var settings = webView.GetSettings();
		settings.SetEnableJavascript(true);
		webView.SetSettings(settings);

		return webView;
	}

	protected override void ConnectHandler(WebKit.WebView platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnLoadChanged += OnLoadChanged;
		platformView.OnLoadFailed += OnLoadFailed;
	}

	protected override void DisconnectHandler(WebKit.WebView platformView)
	{
		platformView.OnLoadChanged -= OnLoadChanged;
		platformView.OnLoadFailed -= OnLoadFailed;
		base.DisconnectHandler(platformView);
	}

	public static void MapSource(WebViewHandler handler, IWebView webView)
	{
		if (webView.Source == null)
		{
			handler.LoadHtml(string.Empty, "about:blank");
			return;
		}

		webView.Source.Load(handler);
	}

	public static void MapUserAgent(WebViewHandler handler, IWebView webView)
	{
		if (handler.PlatformView == null)
			return;

		var settings = handler.PlatformView.GetSettings();
		if (settings == null)
			return;

		settings.SetUserAgent(webView.UserAgent ?? string.Empty);
		handler.PlatformView.SetSettings(settings);
	}

	public static void MapGoBack(WebViewHandler handler, IWebView webView, object? arg)
	{
		if (handler.PlatformView == null || !handler.PlatformView.CanGoBack())
			return;

		handler._currentNavigationEvent = WebNavigationEvent.Back;
		if (webView.Navigating(WebNavigationEvent.Back, handler._lastUrl))
			return;

		handler.PlatformView.GoBack();
	}

	public static void MapGoForward(WebViewHandler handler, IWebView webView, object? arg)
	{
		if (handler.PlatformView == null || !handler.PlatformView.CanGoForward())
			return;

		handler._currentNavigationEvent = WebNavigationEvent.Forward;
		if (webView.Navigating(WebNavigationEvent.Forward, handler._lastUrl))
			return;

		handler.PlatformView.GoForward();
	}

	public static void MapReload(WebViewHandler handler, IWebView webView, object? arg)
	{
		if (handler.PlatformView == null)
			return;

		handler._currentNavigationEvent = WebNavigationEvent.Refresh;
		if (webView.Navigating(WebNavigationEvent.Refresh, handler._lastUrl))
			return;

		handler.PlatformView.Reload();
	}

	public static void MapEval(WebViewHandler handler, IWebView webView, object? arg)
	{
		if (handler.PlatformView == null || arg is not string script || string.IsNullOrWhiteSpace(script))
			return;

		_ = handler.PlatformView.EvaluateJavascriptAsync(script);
	}

	public static void MapEvaluateJavaScriptAsync(WebViewHandler handler, IWebView webView, object? arg)
	{
		if (handler.PlatformView == null || arg is not EvaluateJavaScriptAsyncRequest request)
			return;

		GLib.Functions.IdleAdd(0, () =>
		{
			_ = handler.EvaluateAsync(request);
			return false;
		});
	}

	async Task EvaluateAsync(EvaluateJavaScriptAsyncRequest request)
	{
		try
		{
			var result = await PlatformView.EvaluateJavascriptAsync(request.Script);
			request.SetResult(result?.ToString() ?? string.Empty);
		}
		catch (Exception ex)
		{
			request.SetException(ex);
		}
	}

	void OnLoadChanged(WebKit.WebView sender, WebKit.WebView.LoadChangedSignalArgs args)
	{
		UpdateNavigationState();

		if (args.LoadEvent == WebKit.LoadEvent.Finished)
		{
			VirtualView?.Navigated(_currentNavigationEvent, _lastUrl, WebNavigationResult.Success);
		}
	}

	bool OnLoadFailed(WebKit.WebView sender, WebKit.WebView.LoadFailedSignalArgs args)
	{
		UpdateNavigationState();
		var failedUrl = string.IsNullOrWhiteSpace(args.FailingUri) ? _lastUrl : args.FailingUri;
		VirtualView?.Navigated(_currentNavigationEvent, failedUrl, WebNavigationResult.Failure);
		return false;
	}

	void UpdateNavigationState()
	{
		if (VirtualView == null || PlatformView == null)
			return;

		VirtualView.CanGoBack = PlatformView.CanGoBack();
		VirtualView.CanGoForward = PlatformView.CanGoForward();
	}

	public void LoadHtml(string? html, string? baseUrl)
	{
		if (PlatformView == null || VirtualView == null)
			return;

		_currentNavigationEvent = WebNavigationEvent.NewPage;
		_lastUrl = string.IsNullOrWhiteSpace(baseUrl) ? "about:blank" : baseUrl;

		if (VirtualView.Navigating(WebNavigationEvent.NewPage, _lastUrl))
			return;

		PlatformView.LoadHtml(html ?? string.Empty, _lastUrl);
	}

	public void LoadUrl(string? url)
	{
		if (PlatformView == null || VirtualView == null || string.IsNullOrWhiteSpace(url))
			return;

		_currentNavigationEvent = WebNavigationEvent.NewPage;
		_lastUrl = url;

		if (VirtualView.Navigating(WebNavigationEvent.NewPage, _lastUrl))
			return;

		PlatformView.LoadUri(url);
	}

	static void EnsureWebKitInitialized()
	{
		if (_moduleInitialized)
			return;

		_moduleInitialized = true;
		setenv("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "1", 0);
		WebKit.Module.Initialize();
	}
}
