using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using WebKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class WebViewHandler : MacOSViewHandler<IWebView, WKWebView>, IWebViewDelegate
{
    public static readonly IPropertyMapper<IWebView, WebViewHandler> Mapper =
        new PropertyMapper<IWebView, WebViewHandler>(ViewMapper)
        {
            [nameof(IWebView.Source)] = MapSource,
            [nameof(IWebView.UserAgent)] = MapUserAgent,
        };

    public static readonly CommandMapper<IWebView, WebViewHandler> CommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(IWebView.GoBack)] = MapGoBack,
            [nameof(IWebView.GoForward)] = MapGoForward,
            [nameof(IWebView.Reload)] = MapReload,
            [nameof(IWebView.Eval)] = MapEval,
            [nameof(IWebView.EvaluateJavaScriptAsync)] = MapEvaluateJavaScriptAsync,
        };

    WebViewNavigationDelegate? _navigationDelegate;

    public WebViewHandler() : base(Mapper, CommandMapper)
    {
    }

    protected override WKWebView CreatePlatformView()
    {
        var config = new WKWebViewConfiguration();
        var webView = new WKWebView(CGRect.Empty, config);
        _navigationDelegate = new WebViewNavigationDelegate(this);
        webView.NavigationDelegate = _navigationDelegate;
        return webView;
    }

    protected override void DisconnectHandler(WKWebView platformView)
    {
        platformView.NavigationDelegate = null;
        _navigationDelegate = null;
        base.DisconnectHandler(platformView);
    }

    public void LoadHtml(string? html, string? baseUrl)
    {
        if (html != null)
            PlatformView.LoadHtmlString(html, baseUrl != null ? new NSUrl(baseUrl) : null);
    }

    public void LoadUrl(string? url)
    {
        if (url != null)
        {
            var nsUrl = new NSUrl(url);
            var request = new NSUrlRequest(nsUrl);
            PlatformView.LoadRequest(request);
        }
    }

    static void MapSource(WebViewHandler handler, IWebView webView)
    {
        webView.Source?.Load(handler);
        handler.PlatformView.UpdateCanGoBackForward(webView);
    }

    static void MapUserAgent(WebViewHandler handler, IWebView webView)
    {
        if (webView.UserAgent != null)
            handler.PlatformView.CustomUserAgent = webView.UserAgent;
    }

    static void MapGoBack(WebViewHandler handler, IWebView webView, object? arg)
    {
        if (handler.PlatformView.CanGoBack)
        {
            if (handler._navigationDelegate != null)
                handler._navigationDelegate.CurrentNavigationEvent = WebNavigationEvent.Back;
            handler.PlatformView.GoBack();
        }
        handler.PlatformView.UpdateCanGoBackForward(webView);
    }

    static void MapGoForward(WebViewHandler handler, IWebView webView, object? arg)
    {
        if (handler.PlatformView.CanGoForward)
        {
            if (handler._navigationDelegate != null)
                handler._navigationDelegate.CurrentNavigationEvent = WebNavigationEvent.Forward;
            handler.PlatformView.GoForward();
        }
        handler.PlatformView.UpdateCanGoBackForward(webView);
    }

    static void MapReload(WebViewHandler handler, IWebView webView, object? arg)
    {
        if (handler._navigationDelegate != null)
            handler._navigationDelegate.CurrentNavigationEvent = WebNavigationEvent.Refresh;
        handler.PlatformView.Reload();
    }

    static void MapEval(WebViewHandler handler, IWebView webView, object? arg)
    {
        if (arg is string script)
            handler.PlatformView.EvaluateJavaScriptAsync(script);
    }

    static void MapEvaluateJavaScriptAsync(WebViewHandler handler, IWebView webView, object? arg)
    {
        if (arg is EvaluateJavaScriptAsyncRequest request)
        {
            handler.EvaluateJavaScript(request);
        }
    }

    async void EvaluateJavaScript(EvaluateJavaScriptAsyncRequest request)
    {
        try
        {
            var result = await PlatformView.EvaluateJavaScriptAsync(request.Script);
            var stringResult = result?.ToString() ?? "null";
            request.SetResult(stringResult);
        }
        catch (Exception ex)
        {
            request.SetException(ex);
        }
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var width = double.IsPositiveInfinity(widthConstraint) ? 400 : widthConstraint;
        var height = double.IsPositiveInfinity(heightConstraint) ? 400 : heightConstraint;
        return new Size(width, height);
    }

    class WebViewNavigationDelegate : WKNavigationDelegate
    {
        readonly WebViewHandler _handler;
        public WebNavigationEvent CurrentNavigationEvent { get; set; } = WebNavigationEvent.NewPage;

        public WebViewNavigationDelegate(WebViewHandler handler) => _handler = handler;

        public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        {
            var url = navigationAction.Request.Url?.AbsoluteString ?? string.Empty;
            var virtualView = _handler.VirtualView;

            if (virtualView != null)
            {
                var navigating = virtualView.Navigating(CurrentNavigationEvent, url);
                decisionHandler(navigating ? WKNavigationActionPolicy.Cancel : WKNavigationActionPolicy.Allow);
            }
            else
            {
                decisionHandler(WKNavigationActionPolicy.Allow);
            }
        }

        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            var url = webView.Url?.AbsoluteString ?? string.Empty;
            _handler.VirtualView?.Navigated(CurrentNavigationEvent, url, WebNavigationResult.Success);
            webView.UpdateCanGoBackForward(_handler.VirtualView!);
            CurrentNavigationEvent = WebNavigationEvent.NewPage;
        }

        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            var url = webView.Url?.AbsoluteString ?? string.Empty;
            _handler.VirtualView?.Navigated(CurrentNavigationEvent, url, WebNavigationResult.Failure);
            CurrentNavigationEvent = WebNavigationEvent.NewPage;
        }

        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            var url = webView.Url?.AbsoluteString ?? string.Empty;
            _handler.VirtualView?.Navigated(CurrentNavigationEvent, url, WebNavigationResult.Failure);
            CurrentNavigationEvent = WebNavigationEvent.NewPage;
        }
    }
}

static class WKWebViewExtensions
{
    public static void UpdateCanGoBackForward(this WKWebView webView, IWebView virtualView)
    {
        virtualView.CanGoBack = webView.CanGoBack;
        virtualView.CanGoForward = webView.CanGoForward;
    }
}
