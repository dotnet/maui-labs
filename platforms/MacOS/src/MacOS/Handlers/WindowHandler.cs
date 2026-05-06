using CoreGraphics;
using Foundation;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;
using AppKit;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Flipped NSView used as NSWindow.ContentView so MAUI's top-left coordinate system works.
/// Also observes effective appearance changes to drive light/dark mode switching.
/// </summary>
internal class FlippedNSView : NSView
{
    public FlippedNSView()
    {
        WantsLayer = true;
        PostsFrameChangedNotifications = true;
    }

    public override bool IsFlipped => true;

    /// The MAUI content page hosted in this container, set when MapContent runs.
    internal WeakReference<IView>? ContentView { get; set; }

    public override void SetFrameSize(CGSize newSize)
    {
        base.SetFrameSize(newSize);
        RelayoutContent();
    }

    public override void Layout()
    {
        base.Layout();
        RelayoutContent();
    }

    void RelayoutContent()
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        try
        {
            if (ContentView != null && ContentView.TryGetTarget(out var content))
            {
                content.Measure((double)size.Width, (double)size.Height);
                content.Arrange(new Graphics.Rect(0, 0, (double)size.Width, (double)size.Height));
            }
        }
        catch (Exception ex)
        {
            // AppKit retries Layout() infinitely when exceptions propagate
            Console.Error.WriteLine($"[WindowContentView.RelayoutContent] {ex}");
        }
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        SyncUserAppTheme(EffectiveAppearance);
    }

    internal static void SyncUserAppTheme(NSAppearance appearance)
    {
        if (Application.Current is null)
            return;

        // Only call ThemeChanged() which updates PlatformAppTheme via AppInfo.RequestedTheme.
        // Do NOT set UserAppTheme — that's for programmatic overrides by the developer.
        (Application.Current as IApplication)?.ThemeChanged();
    }
}

/// <summary>
/// Delegates window close events back to the MAUI WindowHandler so that
/// closing via the red button properly fires IWindow.Destroying() and
/// removes the window from the tracked list.
/// </summary>
internal class MacOSWindowDelegate : NSWindowDelegate
{
    readonly WeakReference<WindowHandler> _handlerRef;

    public MacOSWindowDelegate(WindowHandler handler)
    {
        _handlerRef = new WeakReference<WindowHandler>(handler);
    }

    public override bool WindowShouldClose(NSObject sender)
    {
        return true;
    }

    public override void WillClose(NSNotification notification)
    {
        if (!_handlerRef.TryGetTarget(out var handler))
            return;

        var closedWindow = notification.Object as NSWindow;
        handler.OnWindowClosed(closedWindow);
    }
}

public partial class WindowHandler : ElementHandler<IWindow, NSWindow>
{
    public static readonly IPropertyMapper<IWindow, WindowHandler> Mapper =
        new PropertyMapper<IWindow, WindowHandler>(ElementMapper)
        {
            [nameof(IWindow.Title)] = MapTitle,
            [nameof(IWindow.Content)] = MapContent,
            [nameof(IWindow.Width)] = MapSize,
            [nameof(IWindow.Height)] = MapSize,
            [nameof(IWindow.X)] = MapPosition,
            [nameof(IWindow.Y)] = MapPosition,
            [nameof(IWindow.MinimumWidth)] = MapMinMaxSize,
            [nameof(IWindow.MinimumHeight)] = MapMinMaxSize,
            [nameof(IWindow.MaximumWidth)] = MapMinMaxSize,
            [nameof(IWindow.MaximumHeight)] = MapMinMaxSize,
            [MacOSWindow.TitlebarStyleProperty.PropertyName] = MapTitlebarStyle,
            [MacOSWindow.TitlebarTransparentProperty.PropertyName] = MapTitlebarTransparent,
            [MacOSWindow.TitleVisibilityProperty.PropertyName] = MapTitleVisibility,
            [MacOSWindow.FullSizeContentViewProperty.PropertyName] = MapFullSizeContentView,
            [MacOSWindow.TitlebarSeparatorStyleProperty.PropertyName] = MapTitlebarSeparatorStyle,
        };

    FlippedNSView? _contentContainer;
    MacOSToolbarManager? _toolbarManager;
    MacOSModalManager? _modalManager;
    MacOSWindowDelegate? _windowDelegate;
    static int _windowCascadeOffset;

    public WindowHandler() : base(Mapper)
    {
    }

    /// <summary>
    /// Called by MacOSWindowDelegate when the NSWindow is closed (red button or programmatically).
    /// Fires IWindow.Destroying() and removes the window from the tracked list.
    /// </summary>
    internal void OnWindowClosed(NSWindow? closedNsWindow)
    {
        if (VirtualView is IWindow window)
        {
            window.Destroying();

            if (IPlatformApplication.Current is MacOSMauiApplication macApp)
            {
                macApp.RemoveWindow(window);

                // Re-activate the next remaining window so it regains key status
                foreach (var w in macApp.Windows)
                {
                    if (w.Handler?.PlatformView is NSWindow nsWin && nsWin != closedNsWindow && nsWin.IsVisible)
                    {
                        nsWin.MakeKeyAndOrderFront(null);
                        break;
                    }
                }
            }
        }

        UnsubscribeModalEvents();
        UnsubscribePageChanges();
    }

    protected override NSWindow CreatePlatformElement()
    {
        var style = NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable;

        // FullSizeContentView lets content extend behind the titlebar (edge-to-edge).
        // Default is true; set MacOSWindow.FullSizeContentView to false to keep content below titlebar.
        bool fullSizeContent = true;
        if (VirtualView is Window w)
            fullSizeContent = MacOSWindow.GetFullSizeContentView(w);
        if (fullSizeContent)
            style |= NSWindowStyle.FullSizeContentView;

        var window = new NSWindow(
            new CGRect(0, 0, 1280, 720),
            style,
            NSBackingStore.Buffered,
            false);

        window.Center();
        window.ReleasedWhenClosed = false;

        // Cascade additional windows so they don't stack on top of each other
        if (_windowCascadeOffset > 0)
        {
            var origin = window.Frame.Location;
            window.SetFrameOrigin(new CGPoint(origin.X + 20 * _windowCascadeOffset, origin.Y - 20 * _windowCascadeOffset));
        }
        _windowCascadeOffset++;

        window.ToolbarStyle = (NSWindowToolbarStyle)(int)MacOSWindow.GetTitlebarStyle((BindableObject)VirtualView);
        window.TitleVisibility = (NSWindowTitleVisibility)(int)MacOSWindow.GetTitleVisibility((BindableObject)VirtualView);
        window.TitlebarAppearsTransparent = MacOSWindow.GetTitlebarTransparent((BindableObject)VirtualView);

        // Use a flipped NSView as ContentView so subviews use top-left origin
        _contentContainer = new FlippedNSView();
        window.ContentView = _contentContainer;

        // Attach the toolbar manager
        _toolbarManager = new MacOSToolbarManager();
        _toolbarManager.AttachToWindow(window);

        // Create the modal manager
        _modalManager = new MacOSModalManager(_contentContainer, window);

        // Set the window delegate to handle close events
        _windowDelegate = new MacOSWindowDelegate(this);
        window.Delegate = _windowDelegate;

        window.MakeKeyAndOrderFront(null);

        // Set the initial theme from the window's effective appearance
        FlippedNSView.SyncUserAppTheme(window.EffectiveAppearance);

        return window;
    }

    public static void MapTitle(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView != null)
            handler.PlatformView.Title = window.Title ?? string.Empty;
    }

    public static void MapContent(WindowHandler handler, IWindow window)
    {
        if (handler.MauiContext == null || window.Content == null)
            return;

        var page = window.Content;
        var pageHandler = page.ToHandler(handler.MauiContext);
        var pageView = pageHandler.ToPlatformView();

        Console.WriteLine($"[WindowHandler.MapContent] page={page.GetType().Name}, handler={pageHandler.GetType().Name}");

        // Shell or FlyoutPage with NSSplitViewController: set as window's contentViewController
        // so the system provides proper sidebar titlebar integration (traffic lights
        // inside the sidebar, behind-window vibrancy, inset rounded corners).
        NSSplitViewController? splitVC = null;
        if (page is Shell && pageHandler is ShellHandler shellHandler)
            splitVC = shellHandler.SplitViewController;
        else if (page is FlyoutPage && pageHandler is FlyoutPageHandler flyoutHandler)
            splitVC = flyoutHandler.PlatformView.SplitViewController;
        else if (page is FlyoutPage && pageHandler is NativeSidebarFlyoutPageHandler nativeHandler)
            splitVC = nativeHandler.SplitViewController;

        if (splitVC != null)
        {
            // Preserve the window frame — setting contentViewController can resize it
            var savedFrame = handler.PlatformView.Frame;

            handler.PlatformView.ContentViewController = splitVC;

            // Restore the original window frame
            handler.PlatformView.SetFrame(savedFrame, true);

            // Sidebar requires Unified toolbar style for proper titlebar integration.
            if (handler.PlatformView.ToolbarStyle == NSWindowToolbarStyle.Automatic)
                handler.PlatformView.ToolbarStyle = NSWindowToolbarStyle.Unified;

            // Re-attach the toolbar AFTER setting contentViewController so the system
            // recalculates titlebar integration with the NSSplitViewController sidebar.
            var toolbar = handler.PlatformView.Toolbar;
            handler.PlatformView.Toolbar = null;
            handler.PlatformView.Toolbar = toolbar;

            // Pass the split view to the toolbar manager for NSTrackingSeparatorToolbarItem
            handler._toolbarManager?.SetSplitView(splitVC.SplitView);

            // Recreate modal manager with the new content view
            var contentView = handler.PlatformView.ContentView;
            if (contentView != null)
                handler._modalManager = new MacOSModalManager(contentView, handler.PlatformView);

            handler.SubscribeModalEvents(window);
            handler.ObservePageChanges(page);
            handler.RefreshToolbar();
            return;
        }

        if (handler._contentContainer != null)
        {
            foreach (var subview in handler._contentContainer.Subviews)
                subview.RemoveFromSuperview();

            pageView.Frame = handler._contentContainer.Bounds;
            pageView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
            handler._contentContainer.AddSubview(pageView);
            handler._contentContainer.ContentView = new WeakReference<IView>(page);

            // Measure and arrange the content so handlers like Shell can set up layout
            var bounds = handler._contentContainer.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                page.Measure((double)bounds.Width, (double)bounds.Height);
                page.Arrange(new Graphics.Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
            }
        }

        // Subscribe to modal events on the Window
        handler.SubscribeModalEvents(window);

        // Subscribe to page-level navigation events so toolbar refreshes on every page change
        handler.ObservePageChanges(page);
        handler.RefreshToolbar();
    }

    public static void MapSize(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null)
            return;

        var width = window.Width >= 0 ? window.Width : handler.PlatformView.Frame.Width;
        var height = window.Height >= 0 ? window.Height : handler.PlatformView.Frame.Height;
        var frame = handler.PlatformView.Frame;
        // NSWindow origin is bottom-left; keep the top-left corner stable
        var newY = frame.Y + frame.Height - (nfloat)height;
        handler.PlatformView.SetFrame(new CGRect(frame.X, newY, (nfloat)width, (nfloat)height), true);
    }

    public static void MapPosition(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null)
            return;

        if (window.X >= 0 && window.Y >= 0)
        {
            var screen = handler.PlatformView.Screen ?? NSScreen.MainScreen;
            if (screen != null)
            {
                // Convert MAUI top-left origin to AppKit bottom-left origin
                var frame = handler.PlatformView.Frame;
                var screenHeight = screen.Frame.Height;
                var newY = screenHeight - (nfloat)window.Y - frame.Height;
                handler.PlatformView.SetFrameOrigin(new CGPoint((nfloat)window.X, newY));
            }
        }
    }

    void ObservePageChanges(IView content)
    {
        // Unsubscribe previous
        UnsubscribePageChanges();
        _observedContent = content;

        SubscribePageChanges(content);
    }

    IView? _observedContent;
    readonly List<(object source, string eventName)> _subscriptions = new();

    void SubscribePageChanges(IView? view)
    {
        switch (view)
        {
            case Shell shell:
                shell.Navigated += OnShellNavigated;
                _subscriptions.Add((shell, "Navigated"));
                break;

            case TabbedPage tabbed:
                tabbed.CurrentPageChanged += OnCurrentPageChanged;
                _subscriptions.Add((tabbed, nameof(TabbedPage.CurrentPageChanged)));
                SubscribePageChanges((IView?)tabbed.CurrentPage);
                break;

            case NavigationPage nav:
                nav.Pushed += OnNavigationChanged;
                nav.Popped += OnNavigationChanged;
                _subscriptions.Add((nav, "Pushed"));
                _subscriptions.Add((nav, "Popped"));
                SubscribePageChanges((IView?)nav.CurrentPage);
                break;

            case FlyoutPage flyout:
                flyout.PropertyChanged += OnFlyoutPropertyChanged;
                _subscriptions.Add((flyout, "PropertyChanged"));
                SubscribePageChanges((IView?)flyout.Detail);
                break;
        }
    }

    void UnsubscribePageChanges()
    {
        foreach (var (source, eventName) in _subscriptions)
        {
            switch (source)
            {
                case Shell shell:
                    shell.Navigated -= OnShellNavigated;
                    break;
                case TabbedPage tabbed:
                    tabbed.CurrentPageChanged -= OnCurrentPageChanged;
                    break;
                case NavigationPage nav:
                    nav.Pushed -= OnNavigationChanged;
                    nav.Popped -= OnNavigationChanged;
                    break;
                case FlyoutPage flyout:
                    flyout.PropertyChanged -= OnFlyoutPropertyChanged;
                    break;
            }
        }
        _subscriptions.Clear();
    }

    void OnCurrentPageChanged(object? sender, EventArgs e)
    {
        // Tab changed — resubscribe to the new branch and refresh
        if (_observedContent != null)
            ObservePageChanges(_observedContent);
        RefreshToolbar();
    }

    void OnNavigationChanged(object? sender, NavigationEventArgs e)
    {
        // Push/pop — resubscribe and refresh
        if (_observedContent != null)
            ObservePageChanges(_observedContent);
        RefreshToolbar();
    }

    void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        // Shell navigation (GoToAsync, push, pop, sidebar) — refresh toolbar
        if (_observedContent != null)
            ObservePageChanges(_observedContent);
        RefreshToolbar();
    }

    void OnFlyoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyoutPage.Detail))
        {
            if (_observedContent != null)
                ObservePageChanges(_observedContent);
            RefreshToolbar();
        }
    }

    internal void RefreshToolbar()
    {
        if (_toolbarManager == null || _observedContent == null)
            return;

        var page = FindCurrentPage(_observedContent);
        _toolbarManager.SetPage(page);
    }

    /// <summary>
    /// Walks through navigation containers to find the currently visible content page.
    /// </summary>
    static Page? FindCurrentPage(IView? view)
    {
        return view switch
        {
            Shell shell => GetShellVisiblePage(shell),
            FlyoutPage flyout => FindCurrentPage((IView?)flyout.Detail),
            TabbedPage tabbed => FindCurrentPage((IView?)tabbed.CurrentPage),
            NavigationPage nav => FindCurrentPage((IView?)nav.CurrentPage),
            Page page => page,
            _ => null,
        };
    }

    static Page? GetShellVisiblePage(Shell shell)
    {
        // Check for pushed pages on the ShellSection navigation stack
        if (shell.CurrentItem?.CurrentItem is ShellSection section)
        {
            var navStack = section.Navigation?.NavigationStack;
            if (navStack != null && navStack.Count > 1)
                return navStack[^1];
        }
        return shell.CurrentPage;
    }

    public static void MapTitlebarStyle(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null || window is not BindableObject bo)
            return;

        handler.PlatformView.ToolbarStyle = (NSWindowToolbarStyle)(int)MacOSWindow.GetTitlebarStyle(bo);
    }

    public static void MapTitlebarTransparent(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null || window is not BindableObject bo)
            return;

        handler.PlatformView.TitlebarAppearsTransparent = MacOSWindow.GetTitlebarTransparent(bo);
    }

    public static void MapTitleVisibility(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null || window is not BindableObject bo)
            return;

        handler.PlatformView.TitleVisibility = (NSWindowTitleVisibility)(int)MacOSWindow.GetTitleVisibility(bo);
    }

    public static void MapFullSizeContentView(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null || window is not BindableObject bo)
            return;

        var nsWindow = handler.PlatformView;
        bool fullSize = MacOSWindow.GetFullSizeContentView(bo);

        if (fullSize)
            nsWindow.StyleMask |= NSWindowStyle.FullSizeContentView;
        else
            nsWindow.StyleMask &= ~NSWindowStyle.FullSizeContentView;
    }

    public static void MapTitlebarSeparatorStyle(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null || window is not BindableObject bo)
            return;

        var style = MacOSWindow.GetTitlebarSeparatorStyle(bo);
        handler.PlatformView.TitlebarSeparatorStyle = (NSTitlebarSeparatorStyle)(int)style;
    }

    public static void MapMinMaxSize(WindowHandler handler, IWindow window)
    {
        if (handler.PlatformView == null)
            return;

        var minW = window.MinimumWidth > 0 ? (nfloat)window.MinimumWidth : 0;
        var minH = window.MinimumHeight > 0 ? (nfloat)window.MinimumHeight : 0;
        handler.PlatformView.MinSize = new CGSize(minW, minH);

        var maxW = window.MaximumWidth > 0 && window.MaximumWidth < double.MaxValue ? (nfloat)window.MaximumWidth : nfloat.MaxValue;
        var maxH = window.MaximumHeight > 0 && window.MaximumHeight < double.MaxValue ? (nfloat)window.MaximumHeight : nfloat.MaxValue;
        handler.PlatformView.MaxSize = new CGSize(maxW, maxH);
    }

    Window? _subscribedWindow;

    void SubscribeModalEvents(IWindow window)
    {
        UnsubscribeModalEvents();

        if (window is Window w)
        {
            _subscribedWindow = w;
            w.ModalPushed += OnModalPushed;
            w.ModalPopped += OnModalPopped;
        }
    }

    void UnsubscribeModalEvents()
    {
        if (_subscribedWindow != null)
        {
            _subscribedWindow.ModalPushed -= OnModalPushed;
            _subscribedWindow.ModalPopped -= OnModalPopped;
            _subscribedWindow = null;
        }
    }

    void OnModalPushed(object? sender, ModalPushedEventArgs e)
    {
        if (_modalManager != null && MauiContext != null)
            _modalManager.PushModal(e.Modal, MauiContext, true);
    }

    void OnModalPopped(object? sender, ModalPoppedEventArgs e)
    {
        if (_modalManager != null)
            _modalManager.PopModal(true);
    }
}
