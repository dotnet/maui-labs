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
    private Task? _initialization;
    private readonly CancellationTokenSource _lifetime = new();
    private int _callbacks;
    private int _matchingBindings;
    private int _acceptedPayloads;
    private string _lastSender = "none";
    private bool _disposed;

    internal Task EnsureInitializedAsync(CoreWebView2 core)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // WinRT can project the same native core through different managed wrappers.
        if (_core is not null && _core != core)
            throw new InvalidOperationException("The CDP response binding belongs to a different native WebView.");
        if (_initialization?.IsFaulted == true)
        {
            if (_receiver is not null)
                _receiver.DevToolsProtocolEventReceived -= OnBindingCalled;
            _initialization = null;
        }
        return _initialization ??= InitializeAsync(core);
    }

    private async Task InitializeAsync(CoreWebView2 core)
    {
        _core = core;
        _receiver = core.GetDevToolsProtocolEventReceiver("Runtime.bindingCalled");
        _receiver.DevToolsProtocolEventReceived += OnBindingCalled;
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

    private void OnBindingCalled(CoreWebView2 sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        Interlocked.Increment(ref _callbacks);
        var sameCore = sender is not null && sender == _core;
        Volatile.Write(ref _lastSender,
            $"null={sender is null}, managedSame={ReferenceEquals(sender, _core)}, nativeSame={sameCore}");
        if (_disposed || !sameCore) return;
        try
        {
            using var message = JsonDocument.Parse(args.ParameterObjectAsJson);
            if (message.RootElement.ValueKind == JsonValueKind.Object &&
                message.RootElement.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String && name.GetString() == channel.BindingName)
            {
                Interlocked.Increment(ref _matchingBindings);
                if (message.RootElement.TryGetProperty("payload", out var data) &&
                    data.ValueKind == JsonValueKind.String && data.GetString() is { } payload &&
                    channel.TryReceive(payload))
                    Interlocked.Increment(ref _acceptedPayloads);
            }
        }
        catch (JsonException ex)
        {
            logError("Invalid native CDP binding event JSON.", ex);
        }
    }

    private string DescribeFailure(string stage)
        => $"Native CDP response binding setup failed during {stage}; " +
            $"callbacks={Volatile.Read(ref _callbacks)}, matchingBindings={Volatile.Read(ref _matchingBindings)}, " +
            $"acceptedPayloads={Volatile.Read(ref _acceptedPayloads)}, lastSender=({Volatile.Read(ref _lastSender)}).";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_receiver is not null)
            _receiver.DevToolsProtocolEventReceived -= OnBindingCalled;
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
