using System.Globalization;
using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.MacOS.Controls;
using WebKit;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class BlazorWebViewHandler : MacOSViewHandler<MacOSBlazorWebView, WKWebView>
{
    public static readonly IPropertyMapper<MacOSBlazorWebView, BlazorWebViewHandler> Mapper =
        new PropertyMapper<MacOSBlazorWebView, BlazorWebViewHandler>(ViewMapper)
        {
            [nameof(MacOSBlazorWebView.ContentInsets)] = MapContentInsets,
            [nameof(MacOSBlazorWebView.HideScrollPocketOverlay)] = MapHideScrollPocketOverlay,
        };

    internal static string AppOrigin { get; } = "app://0.0.0.1/";
    internal static Uri AppOriginUri { get; } = new(AppOrigin);

    private const string BlazorInitScript = @"
        window.__receiveMessageCallbacks = [];
        window.__dispatchMessageCallback = function(message) {
            window.__receiveMessageCallbacks.forEach(function(callback) { callback(message); });
        };
        window.external = {
            sendMessage: function(message) {
                window.webkit.messageHandlers.webwindowinterop.postMessage(message);
            },
            receiveMessage: function(callback) {
                window.__receiveMessageCallbacks.push(callback);
            }
        };

        Blazor.start();

        (function () {
            window.onpageshow = function(event) {
                if (event.persisted) {
                    window.location.reload();
                }
            };
        })();
    ";

    static readonly NSString WindowKey = new("window");

    MacOSWebViewManager? _webviewManager;
    ContentInsetsWindowObserver? _contentInsetsWindowObserver;
    TitlebarWindowObserver? _titlebarWindowObserver;

    string? HostPage => VirtualView?.HostPage;
    new IServiceProvider? Services => MauiContext?.Services;

    public BlazorWebViewHandler() : base(Mapper)
    {
    }

    protected override WKWebView CreatePlatformView()
    {
        var config = new WKWebViewConfiguration();

        config.UserContentController.AddScriptMessageHandler(
            new WebViewScriptMessageHandler(MessageReceived), "webwindowinterop");
        config.UserContentController.AddUserScript(new WKUserScript(
            new NSString(BlazorInitScript), WKUserScriptInjectionTime.AtDocumentEnd, true));

        config.SetUrlSchemeHandler(new SchemeHandler(this), urlScheme: "app");

        var webview = new WKWebView(CGRect.Empty, config);
        config.Preferences.SetValueForKey(NSObject.FromObject(true), new NSString("developerExtrasEnabled"));

        // Allow transparent backgrounds — the page CSS controls what's visible
        webview.SetValueForKey(NSObject.FromObject(false), new NSString("drawsBackground"));

        return webview;
    }

    protected override void ConnectHandler(WKWebView platformView)
    {
        base.ConnectHandler(platformView);
        StartWebViewCoreIfPossible();

        // Apply initial content insets
        if (VirtualView is MacOSBlazorWebView macView)
        {
            MapContentInsets(this, macView);
            MapHideScrollPocketOverlay(this, macView);
        }

        // Install titlebar drag overlay so the window is draggable
        // even when WKWebView covers the titlebar area (FullSizeContentView)
        InstallTitlebarDragOverlay(platformView);
    }

    protected override void DisconnectHandler(WKWebView platformView)
    {
        RemoveContentInsetsWindowObserver(platformView);
        RemoveTitlebarWindowObserver(platformView);

        _titlebarDragOverlay?.RemoveFromSuperview();
        _titlebarDragOverlay = null;

        platformView.StopLoading();

        // Remove the script message handler to break the retain cycle between
        // WKUserContentController and the WebViewScriptMessageHandler.
        platformView.Configuration.UserContentController.RemoveScriptMessageHandler("webwindowinterop");

        var webviewManager = System.Threading.Interlocked.Exchange(ref _webviewManager, null);
        if (webviewManager != null)
        {
            try
            {
                webviewManager.DisposeAsync().AsTask().ContinueWith(_ => { });
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        base.DisconnectHandler(platformView);
    }

    void MessageReceived(Uri uri, string message)
    {
        System.Threading.Volatile.Read(ref _webviewManager)?.MessageReceivedInternal(uri, message);
    }

    void StartWebViewCoreIfPossible()
    {
        var hostPage = HostPage;
        var services = Services;
        if (hostPage == null || services == null || System.Threading.Volatile.Read(ref _webviewManager) != null)
            return;

        var contentRootDir = Path.GetDirectoryName(hostPage) ?? string.Empty;
        var hostPageRelativePath = Path.GetRelativePath(contentRootDir, hostPage);

        var fileProvider = new MacOSMauiAssetFileProvider(contentRootDir);

        var dispatcher = services.GetService<IDispatcher>()
            ?? new Microsoft.Maui.Platforms.MacOS.Platform.MacOSDispatcher();

        var jsComponents = new Microsoft.AspNetCore.Components.Web.JSComponentConfigurationStore();

        var webviewManager = new MacOSWebViewManager(
            PlatformView,
            services,
            new MacOSBlazorDispatcher(dispatcher),
            fileProvider,
            jsComponents,
            contentRootDir,
            hostPageRelativePath);

        foreach (var rootComponent in VirtualView.RootComponents)
        {
            if (rootComponent.ComponentType != null && rootComponent.Selector != null)
            {
                var parameters = rootComponent.Parameters != null
                    ? ParameterView.FromDictionary(rootComponent.Parameters)
                    : ParameterView.Empty;
                _ = webviewManager.AddRootComponentAsync(rootComponent.ComponentType, rootComponent.Selector, parameters);
            }
        }

        System.Threading.Volatile.Write(ref _webviewManager, webviewManager);
        webviewManager.Navigate(VirtualView.StartPath);
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var width = double.IsPositiveInfinity(widthConstraint) ? 400 : widthConstraint;
        var height = double.IsPositiveInfinity(heightConstraint) ? 400 : heightConstraint;
        return new Size(width, height);
    }

    public static void MapContentInsets(BlazorWebViewHandler handler, MacOSBlazorWebView view)
    {
        if (handler.PlatformView == null)
            return;

        var insets = view.ContentInsets;
        var wkWebView = handler.PlatformView;

        // If insets are all zero, auto-calculate from toolbar height when the window is available
        if (insets.Top == 0 && insets.Left == 0 && insets.Bottom == 0 && insets.Right == 0)
        {
            handler.ApplyAutoContentInsets(wkWebView);
        }
        else
        {
            handler.RemoveContentInsetsWindowObserver(wkWebView);
            ApplyContentInsets(wkWebView, insets);
        }

        // Setting content insets can cause WKWebView to recreate its scroll pocket
        // subviews, undoing any prior hiding. Re-apply if HideScrollPocketOverlay is on.
        if (view.HideScrollPocketOverlay)
        {
            CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
                ApplyScrollPocketVisibility(wkWebView, true));
        }
    }

    void ApplyAutoContentInsets(WKWebView wkWebView)
    {
        var window = wkWebView.Window;
        if (window == null)
        {
            // View isn't in a window yet — observe until it is
            if (_contentInsetsWindowObserver == null)
            {
                _contentInsetsWindowObserver = new ContentInsetsWindowObserver(this, wkWebView);
                wkWebView.AddObserver(_contentInsetsWindowObserver,
                    WindowKey, NSKeyValueObservingOptions.New, IntPtr.Zero);
            }
            return;
        }

        RemoveContentInsetsWindowObserver(wkWebView);

        if (!window.StyleMask.HasFlag(NSWindowStyle.FullSizeContentView))
            return;

        var toolbarHeight = window.Frame.Height - window.ContentLayoutRect.Height;
        if (toolbarHeight <= 0)
            return;

        ApplyContentInsets(wkWebView, new Thickness(0, toolbarHeight, 0, 0));

        // Re-hide scroll pocket after insets change (WKWebView may recreate them)
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
            ApplyScrollPocketVisibility(wkWebView, true));
    }

    void RemoveContentInsetsWindowObserver(WKWebView webView)
    {
        if (_contentInsetsWindowObserver == null)
            return;

        webView.RemoveObserver(_contentInsetsWindowObserver, WindowKey);
        _contentInsetsWindowObserver = null;
    }

    sealed class ContentInsetsWindowObserver : NSObject
    {
        readonly BlazorWebViewHandler _handler;
        readonly WKWebView _webView;

        public ContentInsetsWindowObserver(BlazorWebViewHandler handler, WKWebView webView)
        {
            _handler = handler;
            _webView = webView;
        }

        public override void ObserveValue(NSString keyPath, NSObject ofObject,
            NSDictionary change, IntPtr context)
        {
            if (_webView.Window != null)
            {
                _handler.ApplyAutoContentInsets(_webView);
            }
        }
    }

    static void ApplyContentInsets(WKWebView wkWebView, Thickness insets)
    {
        // Use objc_msgSend to call setObscuredContentInsets: directly (macOS 14+)
        var selector = new ObjCRuntime.Selector("setObscuredContentInsets:");
        if (wkWebView.RespondsToSelector(selector))
        {
            _objc_msgSend_NSEdgeInsets(wkWebView.Handle, selector.Handle,
                new NSEdgeInsets((nfloat)insets.Top, (nfloat)insets.Left,
                                (nfloat)insets.Bottom, (nfloat)insets.Right));
            return;
        }

        // Fallback: _setTopContentInset: (private, older macOS versions)
        if (insets.Top > 0)
        {
            var topSelector = new ObjCRuntime.Selector("_setTopContentInset:");
            if (wkWebView.RespondsToSelector(topSelector))
            {
                _objc_msgSend_nfloat(wkWebView.Handle, topSelector.Handle, (nfloat)insets.Top);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport(ObjCRuntime.Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern void _objc_msgSend_nfloat(IntPtr receiver, IntPtr selector, nfloat arg1);

    [System.Runtime.InteropServices.DllImport(ObjCRuntime.Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern void _objc_msgSend_NSEdgeInsets(IntPtr receiver, IntPtr selector, NSEdgeInsets arg1);

    public static void MapHideScrollPocketOverlay(BlazorWebViewHandler handler, MacOSBlazorWebView view)
    {
        if (handler.PlatformView == null)
            return;

        var wkWebView = handler.PlatformView;
        var hide = view.HideScrollPocketOverlay;

        // The scroll pocket views may not exist yet if the WKWebView hasn't been laid out.
        // Defer until the next layout pass to ensure the subview hierarchy is populated.
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
            ApplyScrollPocketVisibility(wkWebView, hide));
    }

    static void ApplyScrollPocketVisibility(WKWebView wkWebView, bool hide)
    {
        static void SetVisibility(NSView view, bool hidden)
        {
            var name = view.Class.Name;
            // NSScrollPocket is the container for the scroll pocket overlay views.
            // BackdropView in WKFlippedView also contributes to the overlay.
            if (name.Contains("ScrollPocket") || name.Contains("BackdropView"))
            {
                view.Hidden = hidden;
                return;
            }
            foreach (var sub in view.Subviews)
                SetVisibility(sub, hidden);
        }

        foreach (var child in wkWebView.Subviews)
            SetVisibility(child, hide);
    }

    sealed class WebViewScriptMessageHandler : NSObject, IWKScriptMessageHandler
    {
        readonly Action<Uri, string> _messageReceivedAction;

        public WebViewScriptMessageHandler(Action<Uri, string> messageReceivedAction)
        {
            _messageReceivedAction = messageReceivedAction;
        }

        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            _messageReceivedAction(AppOriginUri, ((NSString)message.Body).ToString());
        }
    }

    sealed class SchemeHandler : NSObject, IWKUrlSchemeHandler
    {
        readonly BlazorWebViewHandler _handler;

        public SchemeHandler(BlazorWebViewHandler handler) => _handler = handler;

        [Export("webView:startURLSchemeTask:")]
        public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
        {
            var url = urlSchemeTask.Request.Url?.AbsoluteString;
            if (string.IsNullOrEmpty(url))
            {
                urlSchemeTask.DidFailWithError(new NSError(NSError.NSUrlErrorDomain, -1));
                return;
            }

            try
            {
                var responseBytes = GetResponseBytes(url, out var contentType, out var statusCode);
                using var dic = new NSMutableDictionary<NSString, NSString>();

                if (statusCode == 200)
                {
                    dic.Add((NSString)"Content-Length", (NSString)responseBytes.Length.ToString(CultureInfo.InvariantCulture));
                    dic.Add((NSString)"Content-Type", (NSString)contentType);
                    dic.Add((NSString)"Cache-Control", (NSString)"no-cache, max-age=0, must-revalidate, no-store");
                }
                else
                {
                    dic.Add((NSString)"Content-Length", (NSString)"0");
                    dic.Add((NSString)"Content-Type", (NSString)"text/plain");
                }

                if (urlSchemeTask.Request.Url != null)
                {
                    using var response = new NSHttpUrlResponse(urlSchemeTask.Request.Url, statusCode, "HTTP/1.1", dic);
                    urlSchemeTask.DidReceiveResponse(response);
                }

                urlSchemeTask.DidReceiveData(NSData.FromArray(statusCode == 200 ? responseBytes : Array.Empty<byte>()));
                urlSchemeTask.DidFinish();
            }
            catch (Exception ex)
            {
                urlSchemeTask.DidFailWithError(new NSError(NSError.NSUrlErrorDomain, -1, NSDictionary.FromObjectAndKey(
                    new NSString(ex.Message), NSError.LocalizedDescriptionKey)));
            }
        }

        byte[] GetResponseBytes(string? url, out string contentType, out int statusCode)
        {
            contentType = string.Empty;

            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                statusCode = 400;
                return Array.Empty<byte>();
            }

            // Don't fall back to host page for framework/content files
            var allowFallbackOnHostPage = AppOriginUri.IsBaseOf(uri)
                && !uri.AbsolutePath.StartsWith("/_framework/", StringComparison.Ordinal)
                && !uri.AbsolutePath.StartsWith("/_content/", StringComparison.Ordinal);
            var queryIndex = url.IndexOf('?');
            if (queryIndex >= 0)
                url = url[..queryIndex];

            var webviewManager = System.Threading.Volatile.Read(ref _handler._webviewManager);
            if (webviewManager == null)
            {
                statusCode = 503;
                return Array.Empty<byte>();
            }

            if (webviewManager.TryGetResponseContentInternal(url, allowFallbackOnHostPage, out statusCode, out var statusMsg, out var content, out var headers))
            {
                statusCode = 200;
                using var ms = new MemoryStream();
                content.CopyTo(ms);
                content.Dispose();
                if (!headers.TryGetValue("Content-Type", out contentType))
                    contentType = "application/octet-stream";
                return ms.ToArray();
            }

            statusCode = 404;
            return Array.Empty<byte>();
        }

        [Export("webView:stopURLSchemeTask:")]
        public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
        {
        }
    }

    TitlebarDragOverlayView? _titlebarDragOverlay;

    void InstallTitlebarDragOverlay(WKWebView webView)
    {
        // Defer until the view is in a window so we can read the titlebar height
        void TryInstall()
        {
            var window = webView.Window;
            if (window == null)
            {
                // View isn't in a window yet — observe via viewDidMoveToWindow
                if (_titlebarWindowObserver == null)
                {
                    _titlebarWindowObserver = new TitlebarWindowObserver(this, webView);
                    webView.AddObserver(_titlebarWindowObserver,
                        WindowKey, NSKeyValueObservingOptions.New, IntPtr.Zero);
                }
                return;
            }

            RemoveTitlebarWindowObserver(webView);

            if (!window.StyleMask.HasFlag(NSWindowStyle.FullSizeContentView))
                return;

            // The draggable zone covers the titlebar + toolbar area.
            // frame.Height - contentLayoutRect.Height gives that combined height.
            var overlayHeight = window.Frame.Height - window.ContentLayoutRect.Height;
            if (overlayHeight <= 0)
                overlayHeight = 38;

            // Add overlay to the window's themeFrame (the top-level content view)
            // so it covers the titlebar/toolbar zone even when the WebView is
            // positioned below the toolbar by a split view controller.
            // The overlay passes through events that land on toolbar items.
            var themeFrame = window.ContentView?.Superview;
            if (themeFrame == null)
                return;

            _titlebarDragOverlay?.RemoveFromSuperview();
            _titlebarDragOverlay = new TitlebarDragOverlayView(overlayHeight);
            _titlebarDragOverlay.TranslatesAutoresizingMaskIntoConstraints = false;
            themeFrame.AddSubview(_titlebarDragOverlay, NSWindowOrderingMode.Above, null);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                _titlebarDragOverlay.LeadingAnchor.ConstraintEqualTo(themeFrame.LeadingAnchor),
                _titlebarDragOverlay.TrailingAnchor.ConstraintEqualTo(themeFrame.TrailingAnchor),
                _titlebarDragOverlay.TopAnchor.ConstraintEqualTo(themeFrame.TopAnchor),
                _titlebarDragOverlay.HeightAnchor.ConstraintEqualTo(overlayHeight),
            });
        }

        TryInstall();
    }

    void RemoveTitlebarWindowObserver(WKWebView webView)
    {
        if (_titlebarWindowObserver == null)
            return;

        webView.RemoveObserver(_titlebarWindowObserver, WindowKey);
        _titlebarWindowObserver = null;
    }

    sealed class TitlebarWindowObserver : NSObject
    {
        readonly BlazorWebViewHandler _handler;
        readonly WKWebView _webView;

        public TitlebarWindowObserver(BlazorWebViewHandler handler, WKWebView webView)
        {
            _handler = handler;
            _webView = webView;
        }

        public override void ObserveValue(NSString keyPath, NSObject ofObject,
            NSDictionary change, IntPtr context)
        {
            if (_webView.Window != null)
            {
                _handler.InstallTitlebarDragOverlay(_webView);
            }
        }
    }

    /// <summary>
    /// Transparent overlay that captures mouse events in the titlebar zone
    /// and initiates window drag. All other events pass through to the WKWebView.
    /// </summary>
    sealed class TitlebarDragOverlayView : NSView
    {
        readonly nfloat _titlebarHeight;

        public TitlebarDragOverlayView(nfloat titlebarHeight)
        {
            _titlebarHeight = titlebarHeight;
        }

        public override NSView HitTest(CGPoint point)
        {
            // Convert point to our coordinate space
            var localPoint = ConvertPointFromView(point, Superview);

            // Only intercept events in our overlay zone
            if (localPoint.Y < 0 || localPoint.Y > _titlebarHeight
                || localPoint.X < 0 || localPoint.X > Frame.Width)
            {
                return null!;
            }

            // Check if there's a toolbar item under this point — if so, pass through
            // so toolbar buttons remain clickable
            var window = Window;
            if (window?.Toolbar != null)
            {
                // Convert to window coordinates and check if any toolbar item view
                // contains this point
                var windowPoint = ConvertPointToView(localPoint, null);
                foreach (var item in window.Toolbar.Items)
                {
                    if (item.View != null && item.View.Window != null)
                    {
                        var itemPoint = item.View.ConvertPointFromView(windowPoint, null);
                        if (item.View.Bounds.Contains(itemPoint))
                            return null!; // let the toolbar item handle it
                    }
                }
            }

            return this;
        }

        public override void MouseDown(NSEvent theEvent)
        {
            Window?.PerformWindowDrag(theEvent);
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            // Already handled by PerformWindowDrag
        }

        public override void MouseUp(NSEvent theEvent)
        {
            // Double-click on titlebar should zoom the window
            if (theEvent.ClickCount == 2)
            {
                Window?.PerformZoom(this);
            }
        }
    }
}
