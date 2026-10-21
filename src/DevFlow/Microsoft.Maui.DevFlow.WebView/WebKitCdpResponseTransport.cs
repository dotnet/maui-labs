#if IOS || MACCATALYST || MACOS
using Foundation;
using WebKit;

namespace Microsoft.Maui.DevFlow.WebView;

internal sealed class WebKitCdpResponseTransport : IDisposable
{
    private readonly NativeCdpResponseChannel _channel;
    private readonly WebKitCdpResponseBinding _binding;
    private readonly WKUserContentController _controller;
    private readonly WeakReference<WKWebView> _webView;
    private readonly WeakScriptMessageHandler _handler;
    private readonly TimeSpan _initializationTimeout;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _registered;
    private bool _verified;
    private bool _disposed;
    private int _callbacks;
    private int _matchingScopes;
    private int _acceptedPayloads;

    internal WebKitCdpResponseTransport(WKWebView webView, NativeCdpResponseChannel channel,
        TimeSpan initializationTimeout)
    {
        _channel = channel;
        _webView = new(webView);
        _controller = webView.Configuration.UserContentController;
        _binding = new(channel, _controller.Handle, webView.Handle);
        _initializationTimeout = initializationTimeout;
        _handler = new(this);
    }

    internal async Task EnsureInitializedAsync(WKWebView webView)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_webView.TryGetTarget(out var owner) || owner is null || (nint)owner.Handle == 0 || owner.Handle != webView.Handle ||
            webView.Configuration.UserContentController.Handle != _controller.Handle)
            throw new InvalidOperationException("The CDP response handler belongs to a different native WebView or content controller.");

        using var budget = new CancellationTokenSource(_initializationTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, _lifetime.Token);
        var stage = "native handler registration";
        Task? received = null;
        try
        {
            if (!_registered)
            {
                _controller.AddScriptMessageHandler(_handler, _binding.Name);
                _registered = true;
            }
            stage = "document binding installation";
            using var installed = await webView.EvaluateJavaScriptAsync(_binding.InstallScript).WaitAsync(cancellation.Token);
            var status = installed?.ToString();
            if (status == "installed") _verified = false;
            else if (status != "bound")
                throw new InvalidOperationException("The private WebKit response binding is missing or occupied.");
            if (_verified) return;

            var handshake = _channel.BeginHandshake();
            received = handshake.Received;
            stage = "binding handshake dispatch";
            using var sent = await webView.EvaluateJavaScriptAsync(handshake.Script).WaitAsync(cancellation.Token);
            if (sent?.ToString() != "sent")
                throw new InvalidOperationException("Native response binding handshake did not confirm dispatch.");
            stage = "binding handshake callback";
            await received.WaitAsync(cancellation.Token);
            _verified = true;
        }
        catch (OperationCanceledException ex) when (budget.IsCancellationRequested && !_disposed)
        {
            throw new TimeoutException(DescribeFailure(stage), ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"{DescribeFailure(stage)} {ex.Message}", ex);
        }
        finally
        {
            if (received is not null) _channel.EndHandshake(received);
        }
    }

    private string DescribeFailure(string stage)
        => $"WebKit CDP response binding setup failed during {stage}; " +
            $"callbacks={Volatile.Read(ref _callbacks)}, matchingScopes={Volatile.Read(ref _matchingScopes)}, " +
            $"acceptedPayloads={Volatile.Read(ref _acceptedPayloads)}.";

    private void Receive(WKUserContentController controller, WKScriptMessage message)
    {
        Interlocked.Increment(ref _callbacks);
        if (_disposed || !_webView.TryGetTarget(out var owner) || owner is null ||
            (nint)owner.Handle == 0 || owner.Handle != message.WebView?.Handle ||
            controller.Handle != _controller.Handle ||
            owner.Configuration.UserContentController.Handle != _controller.Handle ||
            !message.FrameInfo.MainFrame || message.Name != _binding.Name)
            return;
        Interlocked.Increment(ref _matchingScopes);
        if (message.Body is NSString payload &&
            _binding.TryReceive(controller.Handle, owner.Handle, true, message.Name, payload.ToString()))
            Interlocked.Increment(ref _acceptedPayloads);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _binding.Dispose();
        _lifetime.Cancel();
        try
        {
            if (_registered) _controller.RemoveScriptMessageHandler(_binding.Name);
        }
        finally
        {
            _handler.Dispose();
            _lifetime.Dispose();
        }
    }

    private sealed class WeakScriptMessageHandler(WebKitCdpResponseTransport owner) : NSObject, IWKScriptMessageHandler
    {
        private readonly WeakReference<WebKitCdpResponseTransport> _owner = new(owner);

        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            if (_owner.TryGetTarget(out var transport))
                transport.Receive(userContentController, message);
        }
    }
}
#endif
