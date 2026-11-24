#if NET11_0_OR_GREATER
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

public sealed class MacOSModalNavigationPlatformFactory : IModalNavigationPlatformFactory
{
    public IModalNavigationPlatform? CreateModalNavigationPlatform(IModalNavigationHost host) =>
        new MacOSModalNavigationPlatform(host);
}

sealed class MacOSModalNavigationPlatform : IModalNavigationPlatform
{
    readonly IModalNavigationHost _host;
    bool _disposed;

    public MacOSModalNavigationPlatform(IModalNavigationHost host)
    {
        _host = host;
    }

    public bool IsReady =>
        !_disposed &&
        _host.IsWindowReady &&
        GetManager() is not null;

    public Task PushModalAsync(Page modal, bool animated)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var manager = GetRequiredManager();
        manager.PushModal(modal, _host.MauiContext, animated);
        return Task.CompletedTask;
    }

    public Task PopModalAsync(Page modal, bool animated)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var manager = GetRequiredManager();
        if (ReferenceEquals(manager.TopModal, modal))
            manager.PopModal(animated);
        return Task.CompletedTask;
    }

    public void PageAttached()
    {
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GetManager()?.ClearModalStack();
    }

    MacOSModalManager GetRequiredManager() =>
        GetManager() ?? throw new InvalidOperationException(
            "The AppKit window handler has not created its modal presenter.");

    MacOSModalManager? GetManager() =>
        _host.Window.Handler is WindowHandler handler
            ? handler.ModalManager
            : null;
}
#endif
