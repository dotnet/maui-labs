# Lifecycle Events

Subscribe to macOS application lifecycle events for state management, analytics, and cleanup.

## Available Events

| Event | When it Fires |
|-------|---------------|
| `DidFinishLaunching` | App has finished launching and is ready |
| `DidBecomeActive` | App (or a window) gained focus |
| `DidResignActive` | App (or a window) lost focus |
| `DidHide` | App was hidden (⌘H or Hide menu) |
| `DidUnhide` | App was unhidden / shown again |
| `WillTerminate` | App is about to quit |

## Subscribing to Events

Register lifecycle handlers in `MauiProgram.cs`:

```csharp
// MauiProgram.cs
builder.ConfigureLifecycleEvents(events =>
{
    events.AddMacOS(mac =>
    {
        mac.DidFinishLaunching((notification) =>
        {
            Console.WriteLine("App launched");
        });

        mac.DidBecomeActive((notification) =>
        {
            Console.WriteLine("App became active");
        });

        mac.DidResignActive((notification) =>
        {
            Console.WriteLine("App resigned active");
        });

        mac.DidHide((notification) =>
        {
            Console.WriteLine("App hidden");
        });

        mac.DidUnhide((notification) =>
        {
            Console.WriteLine("App shown");
        });

        mac.WillTerminate((notification) =>
        {
            Console.WriteLine("App terminating — save state");
        });
    });
});
```

## MAUI Application Lifecycle

The standard MAUI `Application` lifecycle methods also work:

```csharp
public class MacOSApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage());

        window.Created += (s, e) => Console.WriteLine("Window created");
        window.Activated += (s, e) => Console.WriteLine("Window activated");
        window.Deactivated += (s, e) => Console.WriteLine("Window deactivated");
        window.Stopped += (s, e) => Console.WriteLine("Window stopped");
        window.Resumed += (s, e) => Console.WriteLine("Window resumed");
        window.Destroying += (s, e) => Console.WriteLine("Window destroying");

        return window;
    }
}
```

## Window Close Button

When the user clicks the red close button (traffic light), the platform fires `IWindow.Destroying()` via a `MacOSWindowDelegate`. This allows you to save state or prompt the user before the window closes.

## Event Flow

Typical lifecycle sequence:

```
App start:    DidFinishLaunching → DidBecomeActive
Switch away:  DidResignActive
Switch back:  DidBecomeActive
Hide (⌘H):   DidResignActive → DidHide
Unhide:       DidUnhide → DidBecomeActive
Quit (⌘Q):   DidResignActive → WillTerminate
```
