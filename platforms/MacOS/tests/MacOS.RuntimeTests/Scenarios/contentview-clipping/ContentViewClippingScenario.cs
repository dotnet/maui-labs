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
            TextColor = Colors.Magenta,
            BackgroundColor = Colors.White,
            Padding = 12,
        };
        AbsoluteLayout.SetLayoutBounds(_movedLabel, new Rect(700, 60, 160, 52));

        _world = new AbsoluteLayout
        {
            WidthRequest = 1200,
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
        var nativeWindow = windowContent.Window
            ?? throw new InvalidOperationException("The native content has no AppKit window.");
        var clipNative = Native(_clip);
        var worldNative = Native(_world);
        var labelNative = Native(_movedLabel);
        windowContent.LayoutSubtreeIfNeeded();
        windowContent.DisplayIfNeeded();
        var initial = context.CaptureWindowBitmap(nativeWindow, "initial.png");

        var clips = clipNative.Layer?.MasksToBounds == true;
        _clip.IsClippedToBounds = false;
        var unclipped = clipNative.Layer?.MasksToBounds == false;
        _clip.IsClippedToBounds = true;
        var clipsAgain = clipNative.Layer?.MasksToBounds == true;

        _world.TranslationX = -500;
        _world.TranslationX = -600;
        await RuntimeTestContext.FlushMainQueueAsync();
        windowContent.LayoutSubtreeIfNeeded();
        var translated = context.CaptureWindowBitmap(nativeWindow, "translated.png");
        var outsideClipIsLavender = IsLavender(initial, windowContent, 300, 180);
        var magentaTextPixels = CountMagentaPixels(translated);

        context.WriteJson("native-state.json", new
        {
            clips,
            unclipped,
            clipsAgain,
            outsideClipIsLavender,
            magentaTextPixels,
            clipFrame = clipNative.Frame.ToString(),
            worldFrame = worldNative.Frame.ToString(),
            labelFrame = labelNative.Frame.ToString(),
            labelText = (labelNative as NSTextField)?.StringValue,
        });

        if (!clips && !clipsAgain && outsideClipIsLavender && magentaTextPixels == 0)
        {
            context.Assert(true,
                "Observed paint outside the ContentView and missing text after moving it into view.");
            context.BaselineFailure("contentview.clip-and-redraw",
                "ContentView did not clip, and text moved into view did not render.");
            return;
        }

        context.Assert(clips, "ContentView did not enable native bounds clipping.",
            "contentview.initial-clip");
        context.Assert(unclipped, "ContentView did not disable native bounds clipping.",
            "contentview.disable-clip");
        context.Assert(clipsAgain, "ContentView did not restore native bounds clipping.",
            "contentview.restore-clip");
        context.Assert(!outsideClipIsLavender,
            "Oversized content painted outside the ContentView bounds.",
            "contentview.rendered-clip");
        context.Pass("IsClippedToBounds updates the native layer");

        context.Assert((labelNative as NSTextField)?.StringValue == "Moved into view",
            "The moved native label lost its text.", "contentview.label-text");
        context.Assert(worldNative.Frame.X == -600,
            $"Expected translated native frame, got {worldNative.Frame}.",
            "contentview.translation");
        context.Assert(magentaTextPixels > 5,
            $"Text moved into view did not render; found {magentaTextPixels} magenta text pixels.",
            "contentview.rendered-text");
        context.Pass("Transformed content and text redraw after moving into view");
    }

    static bool IsLavender(RuntimeBitmap image, NSView view, double x, double y)
    {
        var pixelX = Math.Clamp((int)(x * image.Width / view.Bounds.Width), 0, image.Width - 1);
        var pixelY = Math.Clamp((int)(y * image.Height / view.Bounds.Height), 0, image.Height - 1);
        var offset = pixelY * image.BytesPerRow + pixelX * image.SamplesPerPixel;
        var first = image.Pixels[offset];
        var green = image.Pixels[offset + 1];
        var third = image.Pixels[offset + 2];
        return first >= 225 && green is >= 220 and <= 240 && third >= 225
            && Math.Max(first, third) - Math.Min(first, third) >= 10;
    }

    static int CountMagentaPixels(RuntimeBitmap image)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = y * image.BytesPerRow + x * image.SamplesPerPixel;
                if (image.Pixels[offset] > 150 &&
                    image.Pixels[offset + 1] < 140 &&
                    image.Pixels[offset + 2] > 150)
                {
                    count++;
                }
            }
        }
        return count;
    }

    static NSView Native(IView view) =>
        view.Handler?.PlatformView as NSView
        ?? throw new InvalidOperationException($"{view.GetType().Name} has no AppKit view.");
}
