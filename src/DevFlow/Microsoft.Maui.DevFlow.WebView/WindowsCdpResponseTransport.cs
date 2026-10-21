#if WINDOWS
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Microsoft.Maui.DevFlow.WebView;

internal sealed class WindowsCdpResponseTransport(
    NativeCdpResponseChannel channel, Action<string, Exception> logError) : IDisposable
{
    private CoreWebView2? _core;
    private CoreWebView2DevToolsProtocolEventReceiver? _receiver;
    private Task? _initialization;
    private bool _disposed;

    internal Task EnsureInitializedAsync(CoreWebView2 core)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_core is not null && !ReferenceEquals(_core, core))
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
        await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        await core.CallDevToolsProtocolMethodAsync("Runtime.addBinding",
            JsonSerializer.Serialize(new { name = channel.BindingName }));
    }

    private void OnBindingCalled(CoreWebView2DevToolsProtocolEventReceiver sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        if (_disposed) return;
        using var message = JsonDocument.Parse(args.ParameterObjectAsJson);
        if (message.RootElement.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String && name.GetString() == channel.BindingName &&
            message.RootElement.TryGetProperty("payload", out var data) &&
            data.ValueKind == JsonValueKind.String && data.GetString() is { } payload)
            channel.TryReceive(payload);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_receiver is not null)
            _receiver.DevToolsProtocolEventReceived -= OnBindingCalled;
        _ = RemoveBindingAsync();
    }

    private async Task RemoveBindingAsync()
    {
        if (_initialization is null) return;
        try
        {
            await _initialization;
        }
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
    }
}
#endif
