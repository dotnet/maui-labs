using System.Runtime.CompilerServices;
using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MacOS.RuntimeTests.Scenarios.LongPressChildren;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("long-press-children", 5,
            context => new LongPressChildrenScenario().CreateDelegate(context),
            ExpectedAssertions: 13));
}

/// <summary>
/// A LongPressGestureRecognizer on a layout made its container intercept every child hit, so a Button
/// inside stopped clicking and an Entry could not take focus, even for short clicks. Gesture positions
/// also ignored <c>GetPosition(null)</c> and non-View references and returned recognizer-local values.
/// </summary>
sealed class LongPressChildrenScenario : MauiRuntimeScenario
{
    const double Tolerance = 1.0;

    ContentPage _page = null!;
    VerticalStackLayout _pressTarget = null!;
    Label _pressLabel = null!;
    Button _button = null!;
    Entry _entry = null!;
    Grid _tapTarget = null!;
    Label _tapLabel = null!;
    int _clicks;
    int _longPressed;
    int _taps;
    LongPressedEventArgs? _pressedArgs;
    TappedEventArgs? _tapArgs;
    readonly List<GestureStatus> _statuses = [];

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var pressNative = NativeOf(_pressTarget);
        var nativeWindow = pressNative.Window
            ?? throw new InvalidOperationException("Long-press target is not attached to the AppKit window.");
        var buttonNative = NativeOf(_button);

        // Case 1: a short click on a Button inside the long-press layout still clicks it.
        var buttonPoint = CenterInWindow(buttonNative);
        await ClickAsync(nativeWindow, buttonPoint);
        evidence.AppendJson("events.jsonl", new { step = "button-click", _clicks, _longPressed });

        if (_clicks == 0)
        {
            evidence.Assert(true, "Unfixed AppKit container steals the child Button's click.");
            evidence.BaselineFailure("long-press-children.button-click-stolen",
                "BASELINE_LONGPRESS: a LongPressGestureRecognizer on the parent layout swallowed the Button click.");
            return;
        }

        var buttonHit = HitTest(pressNative, buttonPoint);
        evidence.Assert(buttonHit is not null && buttonHit.IsDescendantOf(buttonNative),
            $"Hit testing the Button inside the long-press layout reaches the NSButton; actual {buttonHit?.GetType().Name ?? "null"}.",
            "long-press-children.button-hit");
        evidence.Assert(_clicks == 1 && _longPressed == 0,
            $"Short click expected clicks=1, longPressed=0; actual clicks={_clicks}, longPressed={_longPressed}.",
            "long-press-children.button-click");
        evidence.Pass("button-click");

        // Case 2: an Entry inside the long-press layout still takes focus on click.
        await ClickAsync(nativeWindow, CenterInWindow(NativeOf(_entry)));
        await Task.Delay(50);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.AppendJson("events.jsonl", new { step = "entry-click", focused = _entry.IsFocused, _longPressed });
        evidence.Assert(_entry.IsFocused,
            "Clicking the Entry inside the long-press layout focuses it.",
            "long-press-children.entry-focus");
        evidence.Assert(_longPressed == 0,
            "A short click on the Entry does not raise LongPressed.",
            "long-press-children.entry-no-long-press");
        evidence.Pass("entry-focus");

        // Case 3: holding on the non-interactive Label is routed to the layout's recognizer.
        var labelPoint = CenterInWindow(NativeOf(_pressLabel));
        await PressAndHoldAsync(nativeWindow, labelPoint, TimeSpan.FromMilliseconds(700));
        evidence.AppendJson("events.jsonl", new
        {
            step = "label-long-press", _clicks, _longPressed, statuses = _statuses.Select(s => s.ToString()).ToArray()
        });
        evidence.Assert(_longPressed == 1,
            $"Holding on the Label raises LongPressed once; actual {_longPressed}.",
            "long-press-children.long-pressed");
        evidence.Assert(_statuses.Count >= 2 && _statuses[0] == GestureStatus.Started && _statuses[^1] == GestureStatus.Completed,
            $"LongPressing reports Started ... Completed; actual {string.Join(",", _statuses)}.",
            "long-press-children.long-pressing-states");
        evidence.Assert(_clicks == 1,
            "The long press does not click the Button.",
            "long-press-children.no-extra-click");
        evidence.Pass("label-long-press");

        // Case 4: LongPressedEventArgs.GetPosition follows the MAUI contract.
        var args = _pressedArgs ?? throw new InvalidOperationException("LongPressed args were not captured.");
        var windowPosition = args.GetPosition(null);
        var localPosition = args.GetPosition(_pressTarget);
        var pagePosition = args.GetPosition(_page);
        var detachedPosition = args.GetPosition(new Label());
        var expectedWindow = InView(nativeWindow.ContentView!, labelPoint);
        var expectedLocal = InView(pressNative, labelPoint);
        var expectedPage = InView(NativeOf(_page), labelPoint);
        evidence.WriteJson("positions.json", new
        {
            windowPosition = Describe(windowPosition), expectedWindow = Describe(expectedWindow),
            localPosition = Describe(localPosition), expectedLocal = Describe(expectedLocal),
            pagePosition = Describe(pagePosition), expectedPage = Describe(expectedPage),
            detachedPosition = Describe(detachedPosition)
        });
        evidence.Assert(Near(windowPosition, expectedWindow),
            $"GetPosition(null) is window-relative: expected {Describe(expectedWindow)}, actual {Describe(windowPosition)}.",
            "long-press-children.position-window");
        evidence.Assert(Near(localPosition, expectedLocal) &&
            windowPosition!.Value.X - localPosition!.Value.X >= 24 - Tolerance &&
            windowPosition.Value.Y - localPosition.Value.Y >= 24 - Tolerance,
            $"GetPosition(layout) is layout-relative and offset from the window position: expected {Describe(expectedLocal)}, actual {Describe(localPosition)}.",
            "long-press-children.position-view");
        evidence.Assert(Near(pagePosition, expectedPage),
            $"GetPosition(ContentPage) resolves the page through its handler: expected {Describe(expectedPage)}, actual {Describe(pagePosition)}.",
            "long-press-children.position-page");
        evidence.Assert(detachedPosition is null,
            $"GetPosition(element without a platform view) is null; actual {Describe(detachedPosition)}.",
            "long-press-children.position-unresolved");
        evidence.Pass("long-press-positions");

        // Case 5: TappedEventArgs.GetPosition uses the same contract.
        var tapNative = NativeOf(_tapTarget);
        var tapPoint = CenterInWindow(NativeOf(_tapLabel));
        await ClickAsync(nativeWindow, tapPoint);
        var tapArgs = _tapArgs;
        var tapWindow = tapArgs?.GetPosition(null);
        var tapLocal = tapArgs?.GetPosition(_tapTarget);
        var expectedTapWindow = InView(nativeWindow.ContentView!, tapPoint);
        var expectedTapLocal = InView(tapNative, tapPoint);
        evidence.AppendJson("events.jsonl", new
        {
            step = "tap", _taps, tapWindow = Describe(tapWindow), tapLocal = Describe(tapLocal)
        });
        evidence.Assert(_taps == 1 && Near(tapWindow, expectedTapWindow),
            $"Tap GetPosition(null) is window-relative: expected {Describe(expectedTapWindow)}, actual {Describe(tapWindow)} (taps={_taps}).",
            "long-press-children.tap-position-window");
        evidence.Assert(Near(tapLocal, expectedTapLocal),
            $"Tap GetPosition(Grid) is Grid-relative: expected {Describe(expectedTapLocal)}, actual {Describe(tapLocal)}.",
            "long-press-children.tap-position-view");
        evidence.Pass("tap-positions");
    }

    static NSView NativeOf(VisualElement element) => element.Handler?.PlatformView as NSView
        ?? throw new InvalidOperationException($"{element.GetType().Name} has no AppKit platform view.");

    static CGPoint CenterInWindow(NSView view) => view.ConvertPointToView(
        new CGPoint(view.Bounds.X + view.Bounds.Width / 2, view.Bounds.Y + view.Bounds.Height / 2), null);

    static NSView? HitTest(NSView container, CGPoint windowPoint)
    {
        var superview = container.Superview
            ?? throw new InvalidOperationException("Hit-test container has no superview.");
        return container.HitTest(superview.ConvertPointFromView(windowPoint, null));
    }

    static Point InView(NSView view, CGPoint windowPoint)
    {
        var local = view.ConvertPointFromView(windowPoint, null);
        if (!view.IsFlipped)
            local.Y = view.Bounds.Height - local.Y;
        return new Point(local.X, local.Y);
    }

    static bool Near(Point? actual, Point expected) =>
        actual is { } value &&
        Math.Abs(value.X - expected.X) <= Tolerance &&
        Math.Abs(value.Y - expected.Y) <= Tolerance;

    static string Describe(Point? point) => point is { } value ? $"({value.X:0.##}, {value.Y:0.##})" : "null";

    static async Task ClickAsync(NSWindow window, CGPoint point)
    {
        await PostAndWaitAsync(window, point, NSEventType.LeftMouseDown, NSEventMask.LeftMouseDown);
        await PostAndWaitAsync(window, point, NSEventType.LeftMouseUp, NSEventMask.LeftMouseUp);
        await RuntimeTestContext.FlushMainQueueAsync();
    }

    static async Task PressAndHoldAsync(NSWindow window, CGPoint point, TimeSpan duration)
    {
        await PostAndWaitAsync(window, point, NSEventType.LeftMouseDown, NSEventMask.LeftMouseDown);
        await Task.Delay(duration);
        await RuntimeTestContext.FlushMainQueueAsync();
        await PostAndWaitAsync(window, point, NSEventType.LeftMouseUp, NSEventMask.LeftMouseUp);
        await RuntimeTestContext.FlushMainQueueAsync();
    }

    static async Task PostAndWaitAsync(NSWindow window, CGPoint point, NSEventType type, NSEventMask mask)
    {
        window.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);

        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = NSEvent.AddLocalMonitorForEventsMatchingMask(mask, mouseEvent =>
        {
            dispatched.TrySetResult();
            return mouseEvent;
        });

        try
        {
            var pressure = type == NSEventType.LeftMouseDown ? 1f : 0f;
            using var mouseEvent = NSEvent.MouseEvent(
                type, point, 0, NSProcessInfo.ProcessInfo.SystemUptime, window.WindowNumber,
                window.GraphicsContext, 1, 1, pressure);
            NSApplication.SharedApplication.PostEvent(mouseEvent, false);
            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            NSEvent.RemoveMonitor(monitor);
        }

        await RuntimeTestContext.FlushMainQueueAsync();
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        var longPress = new LongPressGestureRecognizer { MinimumPressDuration = 300 };
        longPress.LongPressed += (_, e) =>
        {
            _longPressed++;
            _pressedArgs = e;
        };
        longPress.LongPressing += (_, e) => _statuses.Add(e.Status);

        _pressLabel = new Label { Text = "Hold here", HeightRequest = 40, VerticalTextAlignment = TextAlignment.Center };
        _button = new Button { Text = "Click me" };
        _button.Clicked += (_, _) => _clicks++;
        _entry = new Entry { Placeholder = "Type here" };

        _pressTarget = new VerticalStackLayout
        {
            AutomationId = "LongPressTarget",
            Padding = 12,
            Spacing = 8,
            BackgroundColor = Colors.LightGray,
            Children = { _pressLabel, _button, _entry }
        };
        _pressTarget.GestureRecognizers.Add(longPress);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            _taps++;
            _tapArgs = e;
        };
        _tapLabel = new Label { Text = "Tap here", HeightRequest = 40, VerticalTextAlignment = TextAlignment.Center };
        _tapTarget = new Grid
        {
            AutomationId = "TapTarget",
            Padding = 12,
            Margin = new Thickness(40, 16, 0, 0),
            BackgroundColor = Colors.LightBlue,
            Children = { _tapLabel }
        };
        _tapTarget.GestureRecognizers.Add(tap);

        _page = new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Children = { _pressTarget, _tapTarget }
            }
        };

        return new(_page)
        {
            Title = "AppKit long press with child controls",
            Width = 480,
            Height = 420
        };
    }
}
