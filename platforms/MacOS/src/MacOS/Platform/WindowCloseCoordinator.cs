namespace Microsoft.Maui.Platforms.MacOS.Platform;

// Kept independent of AppKit so shutdown ordering can be tested with real MAUI handlers.
internal sealed class WindowCloseCoordinator
{
    bool _closing;

    public bool Close(IElementHandler handler, Action<IWindow> removeWindow)
    {
        // The generic ElementHandler.VirtualView getter throws after disconnection;
        // the interface accessor is nullable and safe for late native callbacks.
        if (_closing || handler.VirtualView is not IWindow window)
            return false;

        _closing = true;
        try
        {
            removeWindow(window);
            window.Destroying();
            return true;
        }
        finally
        {
            _closing = false;
        }
    }
}
