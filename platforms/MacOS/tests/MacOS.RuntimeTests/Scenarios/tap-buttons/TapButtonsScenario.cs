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
        ScenarioRegistry.Register(new("tap-buttons", 2, context => new TapButtonsScenario().CreateDelegate(context),
            ExpectedAssertions: 4));
}

sealed class TapButtonsScenario : MauiRuntimeScenario
{
    Border _target = null!;
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

        SendClick(native, nativeWindow, secondary: false);
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

        SendClick(native, nativeWindow, secondary: true);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.AppendJson("clicks.jsonl", new { button = "secondary", _primaryTaps, _secondaryTaps });
        evidence.Assert(_primaryTaps == 1 && _secondaryTaps == 1,
            $"Right click expected primary=1, secondary=1; actual primary={_primaryTaps}, secondary={_secondaryTaps}.",
            "tap-buttons.secondary-click");
        evidence.Assert(recognizers[0].View == native && recognizers[1].View == native,
            "Both native recognizers remain attached after dispatching mouse events.",
            "tap-buttons.recognizers-attached");
        evidence.Pass("secondary-click");
    }

    static void SendClick(NSView view, NSWindow window, bool secondary)
    {
        window.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);

        var point = view.ConvertPointToView(
            new CGPoint(view.Bounds.X + view.Bounds.Width / 2, view.Bounds.Y + view.Bounds.Height / 2), null);
        var down = secondary ? NSEventType.RightMouseDown : NSEventType.LeftMouseDown;
        var up = secondary ? NSEventType.RightMouseUp : NSEventType.LeftMouseUp;

        using var downEvent = NSEvent.MouseEvent(
            down, point, 0, 0, window.WindowNumber, window.GraphicsContext,
            1, 1, 1);
        using var upEvent = NSEvent.MouseEvent(
            up, point, 0, 0, window.WindowNumber, window.GraphicsContext,
            2, 1, 0);
        NSApplication.SharedApplication.PostEvent(downEvent, false);
        NSApplication.SharedApplication.PostEvent(upEvent, false);
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        var primary = new TapGestureRecognizer();
        primary.Tapped += (_, _) => _primaryTaps++;
        var secondary = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        secondary.Tapped += (_, _) => _secondaryTaps++;

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
        _target.GestureRecognizers.Add(secondary);

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
