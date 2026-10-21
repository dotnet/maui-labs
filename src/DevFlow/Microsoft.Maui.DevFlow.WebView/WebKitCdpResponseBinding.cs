using System.Text.Json;

namespace Microsoft.Maui.DevFlow.WebView;

internal sealed class WebKitCdpResponseBinding(
    NativeCdpResponseChannel channel, nint controllerHandle, nint webViewHandle) : IDisposable
{
    private int _disposed;

    internal string Name => channel.BindingName;

    internal string InstallScript => $$"""
        (() => {
            const name = {{JsonSerializer.Serialize(Name)}};
            const handlers = window.webkit && window.webkit.messageHandlers;
            const handler = handlers && handlers[name];
            if (!handler || typeof handler.postMessage !== 'function') return 'missing';
            if (Object.prototype.hasOwnProperty.call(window, name))
                return typeof window[name] === 'function' ? 'bound' : 'collision';
            Object.defineProperty(window, name, {
                configurable: true,
                value: payload => handler.postMessage(payload)
            });
            return 'installed';
        })()
        """;

    internal bool TryReceive(nint controller, nint webView, bool isMainFrame, string name, string payload)
        => Volatile.Read(ref _disposed) == 0 &&
            controllerHandle != 0 && webViewHandle != 0 &&
            controller == controllerHandle && webView == webViewHandle && isMainFrame && name == Name &&
            channel.TryReceive(payload);

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
