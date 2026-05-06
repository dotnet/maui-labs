using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using AppKit;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

[Register("MacOSMauiApplication")]
public abstract class MacOSMauiApplication : NSApplicationDelegate, IPlatformApplication
{
    IApplication _mauiApp = null!;
    readonly List<IWindow> _windows = new();
    MacOSMauiContext? _applicationContext;

    public IServiceProvider Services { get; protected set; } = null!;

    public IApplication Application => _mauiApp;

    /// <summary>
    /// All currently tracked MAUI windows.
    /// </summary>
    public IReadOnlyList<IWindow> Windows => _windows;

    /// <summary>
    /// The application-scoped MAUI context, used to create window scopes.
    /// </summary>
    internal MacOSMauiContext? ApplicationContext => _applicationContext;

    protected abstract MauiApp CreateMauiApp();

    public override void DidFinishLaunching(NSNotification notification)
    {
        try
        {
            IPlatformApplication.Current = this;

            var mauiApp = CreateMauiApp();

            var rootContext = new MacOSMauiContext(mauiApp.Services);
            var applicationContext = rootContext.MakeApplicationScope(this);
            _applicationContext = applicationContext;

            Services = applicationContext.Services;

            _mauiApp = Services.GetRequiredService<IApplication>();

            // Set up the default macOS menu bar (App, Edit, Window) before window creation
            var menuBarOptions = Services.GetService<MacOSMenuBarOptions>();
            MenuBarManager.SetupDefaultMenuBar(menuBarOptions);

            // Wire up ApplicationHandler
            var appHandler = new ApplicationHandler();
            appHandler.SetMauiContext(applicationContext);
            appHandler.SetVirtualView(_mauiApp);

            // Create the window
            CreatePlatformWindow(applicationContext);

            FireLifecycleEvents<MacOSLifecycle.DidFinishLaunching>(del => del(notification));

            OnStarted();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MAUI STARTUP EXCEPTION: {ex}");
            throw;
        }
    }

    [Export("applicationDidBecomeActive:")]
    public void ApplicationDidBecomeActive(NSNotification notification)
    {
        foreach (var w in _windows.ToArray()) w.Activated();
        FireLifecycleEvents<MacOSLifecycle.DidBecomeActive>(del => del(notification));
    }

    [Export("applicationDidResignActive:")]
    public void ApplicationDidResignActive(NSNotification notification)
    {
        foreach (var w in _windows.ToArray()) w.Deactivated();
        FireLifecycleEvents<MacOSLifecycle.DidResignActive>(del => del(notification));
    }

    [Export("applicationDidHide:")]
    public void ApplicationDidHide(NSNotification notification)
    {
        foreach (var w in _windows.ToArray()) w.Stopped();
        FireLifecycleEvents<MacOSLifecycle.DidHide>(del => del(notification));
    }

    [Export("applicationDidUnhide:")]
    public void ApplicationDidUnhide(NSNotification notification)
    {
        foreach (var w in _windows.ToArray()) w.Resumed();
        FireLifecycleEvents<MacOSLifecycle.DidUnhide>(del => del(notification));
    }

    [Export("applicationWillTerminate:")]
    public void ApplicationWillTerminate(NSNotification notification)
    {
        foreach (var w in _windows.ToArray()) w.Destroying();
        FireLifecycleEvents<MacOSLifecycle.WillTerminate>(del => del(notification));
    }

    /// <summary>
    /// Called after the MAUI application and window have been fully initialized.
    /// Override to perform post-startup actions like starting debug agents.
    /// </summary>
    protected virtual void OnStarted() { }

    private void CreatePlatformWindow(MacOSMauiContext applicationContext)
    {
        CreatePlatformWindow(applicationContext, null);
    }

    internal void CreatePlatformWindow(MacOSMauiContext applicationContext, Microsoft.Maui.Handlers.OpenWindowRequest? request)
    {
        var activationState = request?.State is IPersistedState state
            ? new ActivationState(applicationContext, state)
            : new ActivationState(applicationContext);
        var virtualWindow = _mauiApp.CreateWindow(activationState);
        AddWindow(virtualWindow);

        var platformWindow = new NSWindow();
        var windowContext = applicationContext.MakeWindowScope(platformWindow);

        var windowHandler = new WindowHandler();
        windowHandler.SetMauiContext(windowContext);
        windowHandler.SetVirtualView(virtualWindow);

        virtualWindow.Created();
        virtualWindow.Activated();
    }

    internal void AddWindow(IWindow window) => _windows.Add(window);

    internal void RemoveWindow(IWindow window) => _windows.Remove(window);

    void FireLifecycleEvents<TDelegate>(Action<TDelegate> action) where TDelegate : Delegate
    {
        var lifecycleService = Services?.GetService<ILifecycleEventService>();
        if (lifecycleService == null)
            return;

        lifecycleService.InvokeEvents(typeof(TDelegate).Name, action);
    }
}
