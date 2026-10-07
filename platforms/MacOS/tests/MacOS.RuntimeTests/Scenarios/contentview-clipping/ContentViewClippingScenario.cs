using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MacOS.RuntimeTests.Scenarios.ContentViewClipping;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("contentview-clipping", 2,
            context => new ContentViewClippingScenario().CreateDelegate(context),
            ExpectedAssertions: 7));
}

sealed class ContentViewClippingScenario : MauiRuntimeScenario
{
    ContentView _clip = null!;
    AbsoluteLayout _world = null!;
    Label _movedLabel = null!;

    public override Window CreateWindow(IActivationState? activationState)
    {
        _movedLabel = new Label
        {
            Text = "Moved into view",
            TextColor = Colors.Black,
            BackgroundColor = Colors.White,
            Padding = 12,
        };
        AbsoluteLayout.SetLayoutBounds(_movedLabel, new Rect(400, 60, 160, 52));

        _world = new AbsoluteLayout
        {
            WidthRequest = 600,
            HeightRequest = 240,
            BackgroundColor = Colors.Lavender,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Children = { _movedLabel },
        };

        _clip = new ContentView
        {
            Content = _world,
            IsClippedToBounds = true,
            WidthRequest = 240,
            HeightRequest = 160,
        };

        var root = new AbsoluteLayout { BackgroundColor = Colors.White };
        var header = new Label
        {
            Text = "Header must stay visible",
            TextColor = Colors.White,
            BackgroundColor = Colors.DarkRed,
            Padding = 10,
        };
        var neighbor = new Label
        {
            Text = "Neighbor must not be covered",
            TextColor = Colors.White,
            BackgroundColor = Colors.DarkGreen,
            Padding = 10,
        };
        AbsoluteLayout.SetLayoutBounds(header, new Rect(40, 20, 240, 44));
        AbsoluteLayout.SetLayoutBounds(_clip, new Rect(40, 80, 240, 160));
        AbsoluteLayout.SetLayoutBounds(neighbor, new Rect(320, 100, 240, 52));
        root.Children.Add(header);
        root.Children.Add(_clip);
        root.Children.Add(neighbor);

        return new Window(new ContentPage { Content = root })
        {
            Title = "ContentView clipping regression",
            Width = 640,
            Height = 360,
        };
    }

    public override async Task RunAsync(RuntimeTestContext context, Window window)
    {
        await RuntimeTestContext.FlushMainQueueAsync();

        var windowContent = Native(_clip).Window?.ContentView
            ?? throw new InvalidOperationException("ContentView is not attached to the real AppKit window.");
        var clipNative = Native(_clip);
        var worldNative = Native(_world);
        var labelNative = Native(_movedLabel);
        windowContent.LayoutSubtreeIfNeeded();
        windowContent.DisplayIfNeeded();
        context.Capture(windowContent, "initial.png");

        var clips = clipNative.Layer?.MasksToBounds == true;
        _clip.IsClippedToBounds = false;
        var unclipped = clipNative.Layer?.MasksToBounds == false;
        _clip.IsClippedToBounds = true;
        var clipsAgain = clipNative.Layer?.MasksToBounds == true;

        worldNative.DisplayIfNeeded();
        labelNative.DisplayIfNeeded();
        _world.TranslationX = -300;
        var worldInvalidated = worldNative.NeedsDisplay;
        var labelInvalidated = labelNative.NeedsDisplay;

        context.WriteJson("native-state.json", new
        {
            clips,
            unclipped,
            clipsAgain,
            worldInvalidated,
            labelInvalidated,
            clipFrame = clipNative.Frame.ToString(),
            worldFrame = worldNative.Frame.ToString(),
            labelFrame = labelNative.Frame.ToString(),
            labelText = (labelNative as NSTextField)?.StringValue,
        });

        if (!clips && !clipsAgain && !worldInvalidated && !labelInvalidated)
        {
            context.Assert(true,
                "Observed missing bounds clipping and missing transformed-subtree display invalidation.");
            context.BaselineFailure("contentview.clip-and-redraw",
                "ContentView did not clip, and transformed text was not invalidated for display.");
            return;
        }

        context.Assert(clips, "ContentView did not enable native bounds clipping.",
            "contentview.initial-clip");
        context.Assert(unclipped, "ContentView did not disable native bounds clipping.",
            "contentview.disable-clip");
        context.Assert(clipsAgain, "ContentView did not restore native bounds clipping.",
            "contentview.restore-clip");
        context.Pass("IsClippedToBounds updates the native layer");

        context.Assert(worldInvalidated, "Transformed content was not invalidated for display.",
            "contentview.world-redraw");
        context.Assert(labelInvalidated, "Text moved into view was not invalidated for display.",
            "contentview.label-redraw");
        context.Assert((labelNative as NSTextField)?.StringValue == "Moved into view",
            "The moved native label lost its text.", "contentview.label-text");
        context.Assert(worldNative.Layer?.Transform.M41 == -300,
            $"Expected translated native layer, got {worldNative.Layer?.Transform.M41}.",
            "contentview.translation");
        context.Pass("Transformed content and text redraw after moving into view");

        await RuntimeTestContext.FlushMainQueueAsync();
        context.Capture(windowContent, "translated.png");
    }

    static NSView Native(IView view) =>
        view.Handler?.PlatformView as NSView
        ?? throw new InvalidOperationException($"{view.GetType().Name} has no AppKit view.");
}
