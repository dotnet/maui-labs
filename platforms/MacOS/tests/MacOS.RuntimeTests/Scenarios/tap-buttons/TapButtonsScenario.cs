using System.Runtime.CompilerServices;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace MacOS.RuntimeTests.Scenarios.TapButtons;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("tap-buttons", 3, context => new TapButtonsScenario().CreateDelegate(context),
            ExpectedAssertions: 8));
}

sealed class TapButtonsScenario : MauiRuntimeScenario
{
    Border _target = null!;
    TapGestureRecognizer _secondary = null!;
    int _primaryTaps;
    int _secondaryTaps;

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var native = _target.Handler?.PlatformView as NSView
            ?? throw new InvalidOperationException("Tap target has no AppKit platform view.");
        var nativeWindow = native.Window
            ?? throw new InvalidOperationException("Tap target is not attached to the AppKit window.");
        var recognizers = native.GestureRecognizers?
            .OfType<NSClickGestureRecognizer>()
            .ToArray() ?? [];

        await SendClickAsync(native, nativeWindow, secondary: false);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.AppendJson("clicks.jsonl", new { button = "primary", _primaryTaps, _secondaryTaps });

        if (_primaryTaps == 0 && _secondaryTaps == 1)
        {
            evidence.Assert(true, "Unfixed AppKit routes a primary click to the secondary recognizer.");
            evidence.BaselineFailure("tap-buttons.primary-routed-to-secondary",
                "BASELINE_603: left click fired the secondary-only TapGestureRecognizer.");
            return;
        }

        evidence.Assert(recognizers.Length == 2 &&
            recognizers.Any(r => r.ButtonMask == 1) &&
            recognizers.Any(r => r.ButtonMask == 2),
            "AppKit installs distinct primary and secondary click recognizers.",
            "tap-buttons.native-masks");
        evidence.Assert(_primaryTaps == 1 && _secondaryTaps == 0,
            $"Left click expected primary=1, secondary=0; actual primary={_primaryTaps}, secondary={_secondaryTaps}.",
            "tap-buttons.primary-click");
        evidence.Pass("primary-click");

        await SendClickAsync(native, nativeWindow, secondary: true);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.AppendJson("clicks.jsonl", new { button = "secondary", _primaryTaps, _secondaryTaps });
        evidence.Assert(_primaryTaps == 1 && _secondaryTaps == 1,
            $"Right click expected primary=1, secondary=1; actual primary={_primaryTaps}, secondary={_secondaryTaps}.",
            "tap-buttons.secondary-click");
        evidence.Assert(recognizers[0].View == native && recognizers[1].View == native,
            "Both native recognizers remain attached after dispatching mouse events.",
            "tap-buttons.recognizers-attached");
        evidence.Pass("secondary-click");

        _secondary.Buttons = ButtonsMask.Primary;
        evidence.Assert(recognizers.Count(r => r.ButtonMask == 1) == 2,
            "Changing Buttons to Primary updates the attached native recognizer.",
            "tap-buttons.dynamic-primary-mask");
        await SendClickAsync(native, nativeWindow, secondary: true);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.Assert(_primaryTaps == 1 && _secondaryTaps == 1,
            "Right click is ignored after changing the secondary recognizer to Primary.",
            "tap-buttons.dynamic-secondary-ignored");

        _secondary.Buttons = ButtonsMask.Secondary;
        evidence.Assert(recognizers.Count(r => r.ButtonMask == 1) == 1 &&
            recognizers.Count(r => r.ButtonMask == 2) == 1,
            "Changing Buttons back to Secondary restores the native button masks.",
            "tap-buttons.dynamic-secondary-mask");
        await SendClickAsync(native, nativeWindow, secondary: true);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.Assert(_primaryTaps == 1 && _secondaryTaps == 2,
            "Right click fires after changing the attached recognizer back to Secondary.",
            "tap-buttons.dynamic-secondary-click");
        evidence.AppendJson("clicks.jsonl", new
        {
            button = "secondary-after-property-change", _primaryTaps, _secondaryTaps
        });
        evidence.Pass("dynamic-buttons");
    }

    static async Task SendClickAsync(NSView view, NSWindow window, bool secondary)
    {
        window.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);

        var point = view.ConvertPointToView(
            new CGPoint(view.Bounds.X + view.Bounds.Width / 2, view.Bounds.Y + view.Bounds.Height / 2), null);
        var down = secondary ? NSEventType.RightMouseDown : NSEventType.LeftMouseDown;
        var up = secondary ? NSEventType.RightMouseUp : NSEventType.LeftMouseUp;
        var upMask = secondary ? NSEventMask.RightMouseUp : NSEventMask.LeftMouseUp;
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = NSEvent.AddLocalMonitorForEventsMatchingMask(upMask, mouseEvent =>
        {
            dispatched.TrySetResult();
            return mouseEvent;
        });

        try
        {
            using var downEvent = NSEvent.MouseEvent(
                down, point, 0, 0, window.WindowNumber, window.GraphicsContext,
                1, 1, 1);
            using var upEvent = NSEvent.MouseEvent(
                up, point, 0, 0, window.WindowNumber, window.GraphicsContext,
                2, 1, 0);
            NSApplication.SharedApplication.PostEvent(downEvent, false);
            NSApplication.SharedApplication.PostEvent(upEvent, false);
            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            NSEvent.RemoveMonitor(monitor);
        }
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        var primary = new TapGestureRecognizer();
        primary.Tapped += (_, _) => _primaryTaps++;
        _secondary = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        _secondary.Tapped += (_, _) => _secondaryTaps++;

        _target = new Border
        {
            AutomationId = "TapTarget",
            WidthRequest = 240,
            HeightRequest = 120,
            Content = new Label
            {
                Text = "Left click: primary; right click: secondary",
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        _target.GestureRecognizers.Add(primary);
        _target.GestureRecognizers.Add(_secondary);

        return new(new ContentPage
        {
            Content = new Grid
            {
                Padding = 24,
                Children = { _target }
            }
        })
        {
            Title = "AppKit tap button regression",
            Width = 420,
            Height = 240
        };
    }
}
