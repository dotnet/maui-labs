using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace MacOS.RuntimeTests.Scenarios.TapButtons;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("tap-buttons", 3, context => new TapButtonsScenario().CreateDelegate(context),
            ExpectedAssertions: 4));
}

sealed class TapButtonsScenario : MauiRuntimeScenario
{
    Border _target = null!;
    TapGestureRecognizer _secondary = null!;

    public override Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var native = _target.Handler?.PlatformView as NSView
            ?? throw new InvalidOperationException("Tap target has no AppKit platform view.");
        var recognizers = native.GestureRecognizers?
            .OfType<NSClickGestureRecognizer>()
            .ToArray() ?? [];

        if (recognizers.Length == 2 &&
            recognizers.All(r => r.ButtonMask == 1))
        {
            evidence.Assert(true, "Unfixed AppKit leaves both click recognizers on the primary button mask.");
            evidence.BaselineFailure("tap-buttons.primary-routed-to-secondary",
                "BASELINE_603: AppKit click recognizers both used the primary button mask.");
            return Task.CompletedTask;
        }

        evidence.Assert(recognizers.Length == 2 &&
            recognizers.Any(r => r.ButtonMask == 1) &&
            recognizers.Any(r => r.ButtonMask == 2),
            $"AppKit installs distinct primary and secondary click recognizers. " +
            $"Managed gestures={_target.GestureRecognizers.Count}; native recognizers={recognizers.Length}; " +
            $"button masks=[{string.Join(", ", recognizers.Select(r => r.ButtonMask))}].",
            "tap-buttons.native-masks");
        evidence.Assert(recognizers.Length == 2 && recognizers.All(r => r.View == native),
            "Both native recognizers are attached to the target view.",
            "tap-buttons.recognizers-attached");
        evidence.Pass("recognizer-configuration");

        _secondary.Buttons = ButtonsMask.Primary;
        evidence.Assert(recognizers.Count(r => r.ButtonMask == 1) == 2,
            "Changing Buttons to Primary updates the attached native recognizer.",
            "tap-buttons.dynamic-primary-mask");
        evidence.Pass("primary-mask-update");

        _secondary.Buttons = ButtonsMask.Secondary;
        evidence.Assert(recognizers.Count(r => r.ButtonMask == 1) == 1 &&
            recognizers.Count(r => r.ButtonMask == 2) == 1,
            "Changing Buttons back to Secondary restores the native button masks.",
            "tap-buttons.dynamic-secondary-mask");
        evidence.Pass("secondary-mask-update");
        return Task.CompletedTask;
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        var primary = new TapGestureRecognizer();
        _secondary = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };

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
