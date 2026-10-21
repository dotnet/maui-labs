#if WINDOWS
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Microsoft.Maui.DevFlow.WebView;

internal sealed class WindowsCdpResponseTransport(
    NativeCdpResponseChannel channel, TimeSpan initializationTimeout,
    Action<string, Exception> logError) : IDisposable
{
    private CoreWebView2? _core;
    private CoreWebView2DevToolsProtocolEventReceiver? _receiver;
    private global::Windows.Foundation.TypedEventHandler<CoreWebView2,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs>? _handler;
    private NativeCdpEventSubscription? _subscription;
    private Task? _initialization;
    private readonly CancellationTokenSource _lifetime = new();
    private int _callbacks;
    private string _lastSender = "none";
    private bool _disposed;

    internal Task EnsureInitializedAsync(CoreWebView2 core)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_core is not null && _core != core)
            throw new InvalidOperationException("The CDP response binding belongs to a different native WebView.");
        if (_initialization?.IsFaulted == true)
        {
            Unsubscribe();
            _initialization = null;
        }
        return _initialization ??= InitializeAsync(core);
    }

    private async Task InitializeAsync(CoreWebView2 core)
    {
        _core = core;
        var receiver = core.GetDevToolsProtocolEventReceiver("Runtime.bindingCalled");
        var subscription = new NativeCdpEventSubscription(channel);
        _receiver = receiver;
        _subscription = subscription;
        _handler = (sender, args) => OnBindingCalled(receiver, subscription, sender, args);
        receiver.DevToolsProtocolEventReceived += _handler;
        using var budget = new CancellationTokenSource(initializationTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, _lifetime.Token);
        var handshake = channel.BeginHandshake();
        var stage = "Runtime.enable";
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}").AsTask(cancellation.Token);
            stage = "Runtime.addBinding";
            await core.CallDevToolsProtocolMethodAsync("Runtime.addBinding",
                JsonSerializer.Serialize(new { name = channel.BindingName })).AsTask(cancellation.Token);
            stage = "binding handshake dispatch";
            var sent = await core.ExecuteScriptAsync(handshake.Script).AsTask(cancellation.Token);
            if (WebViewDebugServiceBase.WebViewBridge.NormalizeEvaluationResult(sent) != "sent")
                throw new InvalidOperationException("Native response binding handshake did not confirm dispatch.");
            stage = "binding handshake callback";
            await handshake.Received.WaitAsync(cancellation.Token);
        }
        catch (OperationCanceledException ex) when (budget.IsCancellationRequested && !_disposed)
        {
            throw new TimeoutException(DescribeFailure(stage), ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"{DescribeFailure(stage)} {ex.Message}", ex);
        }
        finally { channel.EndHandshake(handshake.Received); }
    }

    private void OnBindingCalled(CoreWebView2DevToolsProtocolEventReceiver receiver,
        NativeCdpEventSubscription subscription, CoreWebView2 sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        Interlocked.Increment(ref _callbacks);
        var sameCore = sender is not null && sender == _core;
        Volatile.Write(ref _lastSender,
            $"null={sender is null}, managedSame={ReferenceEquals(sender, _core)}, nativeSame={sameCore}");
        // The SDK receiver is obtained from this core and owns this exact subscription.
        // WinRT event-sender projections need not share the bound core's interface pointer.
        if (_disposed || !ReferenceEquals(receiver, _receiver) ||
            !ReferenceEquals(subscription, _subscription)) return;
        try
        {
            subscription.TryReceive(args.ParameterObjectAsJson);
        }
        catch (JsonException ex)
        {
            logError("Invalid native CDP binding event JSON.", ex);
        }
    }

    private string DescribeFailure(string stage)
        => $"Native CDP response binding setup failed during {stage}; " +
            $"callbacks={Volatile.Read(ref _callbacks)}, matchingBindings={_subscription?.MatchingBindings ?? 0}, " +
            $"acceptedPayloads={_subscription?.AcceptedPayloads ?? 0}, lastSender=({Volatile.Read(ref _lastSender)}).";

    private void Unsubscribe()
    {
        _subscription?.Dispose();
        _subscription = null;
        if (_receiver is not null && _handler is not null)
            _receiver.DevToolsProtocolEventReceived -= _handler;
        _handler = null;
        _receiver = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Unsubscribe();
        _ = RemoveBindingAsync();
    }

    private async Task RemoveBindingAsync()
    {
        if (_initialization is null)
        {
            _lifetime.Dispose();
            return;
        }
        try
        {
            await _initialization;
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            logError("Native CDP response binding initialization failed before cleanup.", ex);
        }
        try
        {
            await _core!.CallDevToolsProtocolMethodAsync("Runtime.removeBinding",
                JsonSerializer.Serialize(new { name = channel.BindingName }));
        }
        catch (Exception ex)
        {
            logError("Native CDP response binding cleanup failed.", ex);
        }
        finally { _lifetime.Dispose(); }
    }
}
#endif
