using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

public class WindowCloseCoordinatorTests
{
    [Fact]
    public void Close_AfterApplicationDestroysWindow_DoesNotDestroyAgain()
    {
        var window = new Window(new ContentPage());
        var handler = new TestWindowHandler();
        handler.SetVirtualView(window);
        var destroyingCount = 0;
        window.Destroying += (_, _) => destroyingCount++;

        ((IWindow)window).Destroying();

        // Reproduce the state in issue #473, including MAUI's throwing typed getter.
        Assert.Throws<InvalidOperationException>(() => handler.VirtualView);
        var coordinator = new WindowCloseCoordinator();
        Assert.False(coordinator.Close(handler, _ => Assert.Fail("Window was already destroyed.")));
        Assert.Equal(1, destroyingCount);
    }

    [Fact]
    public void Close_ConnectedWindow_RemovesBeforeDestroyingAndDisconnects()
    {
        var window = new Window(new ContentPage());
        var handler = new TestWindowHandler();
        handler.SetVirtualView(window);
        var windows = new List<IWindow> { window };
        var destroyingCount = 0;
        window.Destroying += (_, _) =>
        {
            Assert.Empty(windows);
            destroyingCount++;
        };

        var coordinator = new WindowCloseCoordinator();
        Assert.True(coordinator.Close(handler, w => windows.Remove(w)));

        Assert.Equal(1, destroyingCount);
        Assert.Null(((IElementHandler)handler).VirtualView);
        Assert.Null(window.Handler);
    }

    [Fact]
    public void Close_ReentrantAndRepeatedCallbacks_DestroyOnlyOnce()
    {
        var window = new Window(new ContentPage());
        var handler = new TestWindowHandler();
        handler.SetVirtualView(window);
        var coordinator = new WindowCloseCoordinator();
        var destroyingCount = 0;
        var removalCount = 0;
        void RemoveWindow(IWindow _) => removalCount++;
        window.Destroying += (_, _) =>
        {
            destroyingCount++;
            Assert.False(coordinator.Close(handler, RemoveWindow));
        };

        Assert.True(coordinator.Close(handler, RemoveWindow));
        Assert.False(coordinator.Close(handler, RemoveWindow));
        Assert.Equal(1, destroyingCount);
        Assert.Equal(1, removalCount);
    }

    [Fact]
    public void Close_DisconnectedHandler_DoesNotNotifyWindow()
    {
        var window = new Window(new ContentPage());
        var handler = new TestWindowHandler();
        handler.SetVirtualView(window);
        var destroyingCount = 0;
        window.Destroying += (_, _) => destroyingCount++;
        ((IElementHandler)handler).DisconnectHandler();

        var coordinator = new WindowCloseCoordinator();
        Assert.False(coordinator.Close(handler, _ => Assert.Fail("Handler was disconnected.")));
        Assert.Equal(0, destroyingCount);
    }

    [Fact]
    public void Close_HandlerReconnectedToAnotherWindow_DestroysNewWindow()
    {
        var handler = new TestWindowHandler();
        handler.SetVirtualView(new Window(new ContentPage()));
        var coordinator = new WindowCloseCoordinator();
        Assert.True(coordinator.Close(handler, _ => { }));

        var nextWindow = new Window(new ContentPage());
        var destroyingCount = 0;
        nextWindow.Destroying += (_, _) => destroyingCount++;
        handler.SetVirtualView(nextWindow);

        Assert.True(coordinator.Close(handler, _ => { }));
        Assert.Equal(1, destroyingCount);
        Assert.Null(nextWindow.Handler);
    }

    // Only the native window is substituted; connection/disconnection and Destroying
    // execute the real MAUI implementation, including the throwing VirtualView getter.
    sealed class TestWindowHandler : ElementHandler<IWindow, object>
    {
        public TestWindowHandler() : base(new PropertyMapper<IWindow>())
        {
        }

        protected override object CreatePlatformElement() => new();
    }
}
