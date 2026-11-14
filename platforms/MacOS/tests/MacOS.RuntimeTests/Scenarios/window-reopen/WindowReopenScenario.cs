using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.RuntimeTests.Scenarios.WindowReopen;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("window-reopen", 4, context => new WindowReopenScenario().CreateDelegate(context),
            ExpectedAssertions: 12));
}

/// <summary>
/// Nothing mapped <see cref="IApplication.ActivateWindow"/>, so <c>Application.ActivateWindow</c> could not bring
/// back a minimized window, and <c>applicationShouldHandleReopen:</c> was not implemented, so clicking the Dock icon
/// after closing the last window left a running app with no window and no way to get one.
/// </summary>
sealed class WindowReopenScenario : MauiRuntimeScenario
{
    int _created;

    static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await RuntimeTestContext.FlushMainQueueAsync();
            await Task.Delay(50);
        }
        return condition();
    }

    /// <summary>AppKit ignores Miniaturize while a restore animation is still running, so ask until it takes.</summary>
    static async Task<bool> MinimizeAsync(NSWindow window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!window.IsMiniaturized && DateTime.UtcNow < deadline)
        {
            window.Miniaturize(null);
            await RuntimeTestContext.FlushMainQueueAsync();
            await Task.Delay(250);
        }
        return window.IsMiniaturized;
    }

    static NSWindow NativeWindow(Window window) =>
        (window.Handler?.PlatformView as NSWindow) ?? throw new InvalidOperationException("Window has no NSWindow.");

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var application = window.Parent as Application ?? throw new InvalidOperationException("Window has no Application.");
        var macApp = IPlatformApplication.Current as MacOSMauiApplication ?? throw new InvalidOperationException("No MacOSMauiApplication.");
        var nativeWindow = NativeWindow(window);
        nativeWindow.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.Activate();
        await RuntimeTestContext.FlushMainQueueAsync();

        // Application.ActivateWindow on a minimized window
        var minimized = await MinimizeAsync(nativeWindow);
        evidence.Assert(minimized, "The window minimizes to the Dock.", "window-reopen.minimize");

        application.ActivateWindow(window);
        var restored = await WaitForAsync(() => !nativeWindow.IsMiniaturized);
        evidence.AppendJson("windows.jsonl", new { step = "activate", restored, visible = nativeWindow.IsVisible });
        if (!restored)
        {
            evidence.Assert(true, "Unfixed AppKit ignores Application.ActivateWindow.");
            evidence.BaselineFailure("window-reopen.activate-ignored",
                "BASELINE_ACTIVATE: Application.ActivateWindow left a minimized window in the Dock.");
            return;
        }
        evidence.Assert(nativeWindow.IsVisible, "ActivateWindow restores the minimized window.", "window-reopen.activate");
        evidence.Pass("activate-minimized");

        // Application.ActivateWindow while the app is in the background ("Show window" from a menu-bar item).
        // Deactivate() is only a request under cooperative activation; hiding (⌘H) always gives up active.
        NSApplication.SharedApplication.Hide(NSApplication.SharedApplication);
        var inactive = await WaitForAsync(() => !NSApplication.SharedApplication.Active && !nativeWindow.IsVisible);
        evidence.Assert(inactive, "The hidden app is no longer active.", "window-reopen.deactivate");

        application.ActivateWindow(window);
        var foreground = await WaitForAsync(() => NSApplication.SharedApplication.Active && nativeWindow.IsKeyWindow);
        evidence.AppendJson("windows.jsonl", new { step = "activate-inactive", visible = nativeWindow.IsVisible, active = NSApplication.SharedApplication.Active, key = nativeWindow.IsKeyWindow });
        evidence.Assert(NSApplication.SharedApplication.Active, "ActivateWindow makes the app active.", "window-reopen.activate-app");
        evidence.Assert(foreground, "ActivateWindow makes the window key.", "window-reopen.activate-key");
        evidence.Pass("activate-inactive");

        // Dock icon clicked while the only window is minimized
        minimized = await MinimizeAsync(nativeWindow);
        evidence.Assert(minimized, "The window minimizes again.", "window-reopen.minimize-again");

        var handledByAppKit = macApp.ApplicationShouldHandleReopen(NSApplication.SharedApplication, false);
        restored = await WaitForAsync(() => !nativeWindow.IsMiniaturized && nativeWindow.IsVisible);
        evidence.AppendJson("windows.jsonl", new { step = "reopen-minimized", handledByAppKit, restored, windows = application.Windows.Count, created = _created });
        evidence.Assert(restored, "Reopen restores the minimized window.", "window-reopen.reopen-restores");
        evidence.Assert(application.Windows.Count == 1 && _created == 1,
            $"Reopen does not create a second window; windows={application.Windows.Count}, created={_created}.", "window-reopen.reopen-no-extra");
        evidence.Pass("reopen-minimized");

        // Dock icon clicked after the last window was closed
        var destroyed = false;
        window.Destroying += (_, _) => destroyed = true;
        nativeWindow.Close();
        var closed = await WaitForAsync(() => destroyed && application.Windows.Count == 0 && macApp.Windows.Count == 0);
        evidence.Assert(closed, $"Closing tears the only window down; destroyed={destroyed}, windows={application.Windows.Count}.", "window-reopen.closed");

        macApp.ApplicationShouldHandleReopen(NSApplication.SharedApplication, false);
        var reopened = await WaitForAsync(() => application.Windows.Count == 1);
        var newWindow = reopened ? application.Windows[0] : null;
        var newNative = newWindow?.Handler?.PlatformView as NSWindow;
        evidence.AppendJson("windows.jsonl", new { step = "reopen-closed", reopened, created = _created, visible = newNative?.IsVisible });
        evidence.Assert(reopened && _created == 2, $"Reopen creates a new window through CreateWindow; created={_created}.", "window-reopen.recreated");
        evidence.Assert(newWindow != null && !ReferenceEquals(newWindow, window), "The new window is a fresh MAUI Window.", "window-reopen.fresh-window");
        evidence.Assert(newNative is { IsVisible: true }, "The new window is on screen.", "window-reopen.visible");
        evidence.Pass("reopen-after-close");
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        _created++;
        return new(new ContentPage
        {
            Content = new Label { Text = $"Window {_created}", Margin = 16 }
        })
        {
            Title = "AppKit window reopen regression",
            Width = 360,
            Height = 200
        };
    }
}
