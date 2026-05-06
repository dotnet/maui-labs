using System.Text.Encodings.Web;
using Foundation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using WebKit;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

internal class MacOSWebViewManager : WebViewManager
{
    private readonly WKWebView _webview;
    private readonly string _contentRootRelativeToAppRoot;

    public MacOSWebViewManager(
        WKWebView webview,
        IServiceProvider provider,
        Microsoft.AspNetCore.Components.Dispatcher dispatcher,
        IFileProvider fileProvider,
        JSComponentConfigurationStore jsComponents,
        string contentRootRelativeToAppRoot,
        string hostPageRelativePath)
        : base(provider, dispatcher, BlazorWebViewHandler.AppOriginUri, fileProvider, jsComponents, hostPageRelativePath)
    {
        _webview = webview;
        _contentRootRelativeToAppRoot = contentRootRelativeToAppRoot;
    }

    protected override void NavigateCore(Uri absoluteUri)
    {
        using var nsUrl = new NSUrl(absoluteUri.ToString());
        using var request = new NSUrlRequest(nsUrl);
        _webview.LoadRequest(request);
    }

    protected override void SendMessage(string message)
    {
        var messageJSStringLiteral = JavaScriptEncoder.Default.Encode(message);
        _webview.EvaluateJavaScript(
            $"__dispatchMessageCallback(\"{messageJSStringLiteral}\")",
            (NSObject? result, NSError? error) => { });
    }

    internal void MessageReceivedInternal(Uri uri, string message)
    {
        MessageReceived(uri, message);
    }

    internal bool TryGetResponseContentInternal(string uri, bool allowFallbackOnHostPage, out int statusCode, out string statusMessage, out Stream content, out IDictionary<string, string> headers)
    {
        return TryGetResponseContent(uri, allowFallbackOnHostPage, out statusCode, out statusMessage, out content, out headers);
    }
}
