using System.Runtime.CompilerServices;
using System.Diagnostics;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.RuntimeTests.Scenarios.ContentViewClipping;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("contentview-clipping", 8,
            context => new ContentViewClippingScenario().CreateDelegate(context),
            ExpectedAssertions: 105));
}

sealed class ContentViewClippingScenario : MauiRuntimeScenario
{
    ContentView _clip = null!;
    AbsoluteLayout _world = null!;
    Label _movedLabel = null!;
    Label _controlLabel = null!;

    public override void Configure(MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Label, ObservedLabelHandler>());

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
        _controlLabel = new Label
        {
            Text = "Header must stay visible",
            TextColor = Colors.Cyan,
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
        AbsoluteLayout.SetLayoutBounds(_controlLabel, new Rect(40, 20, 240, 44));
        AbsoluteLayout.SetLayoutBounds(_clip, new Rect(40, 80, 240, 160));
        AbsoluteLayout.SetLayoutBounds(neighbor, new Rect(320, 100, 240, 52));
        root.Children.Add(_controlLabel);
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
        var initial = await CaptureAsync(context, nativeWindow, "initial.png");
        var controlPixels = CountPixels(initial, nativeWindow, Native(_controlLabel), IsCyan);
        context.Assert(controlPixels > 5, $"Independent visible text has {controlPixels} cyan pixels.",
            "contentview.capture-control");

        var clips = clipNative.Layer?.MasksToBounds == true;
        _clip.IsClippedToBounds = false;
        var unclipped = clipNative.Layer?.MasksToBounds == false;
        var disabled = await CaptureAsync(context, nativeWindow, "unclipped.png");
        _clip.IsClippedToBounds = true;
        var clipsAgain = clipNative.Layer?.MasksToBounds == true;
        var restored = await CaptureAsync(context, nativeWindow, "restored.png");

        context.Assert(NSThread.IsMain, "Transform updates must run on AppKit's main thread.",
            "contentview.ui-thread");
        _world.TranslationX = -500;
        _world.TranslationX = -600;
        await RuntimeTestContext.FlushMainQueueAsync();
        windowContent.LayoutSubtreeIfNeeded();
        var translated = await CaptureAsync(context, nativeWindow, "translated.png");
        var outsideRegion = new CGRect(clipNative.Frame.Right + 4, clipNative.Frame.Y + 60, 24, 60);
        var initialOutsidePixels = CountPixels(initial, nativeWindow, windowContent, IsLavender, outsideRegion);
        var disabledOutsidePixels = CountPixels(disabled, nativeWindow, windowContent, IsLavender, outsideRegion);
        var restoredOutsidePixels = CountPixels(restored, nativeWindow, windowContent, IsLavender, outsideRegion);
        var outsideClipIsLavender = initialOutsidePixels > 5;
        // AppKit coordinate conversion sees native frames, not an ancestor CALayer translation.
        var expectedLabelRegion = new CGRect(clipNative.Frame.X + 100, clipNative.Frame.Y + 60, 140, 52);
        var magentaTextPixels = CountPixels(translated, nativeWindow, windowContent, IsMagenta, expectedLabelRegion);
        var translatedOutsideClipIsLavender =
            CountPixels(translated, nativeWindow, windowContent, IsLavender, outsideRegion) > 5;
        var magentaPixelsOutsideClip = CountPixels(translated, nativeWindow, windowContent, IsMagenta)
            - CountPixels(translated, nativeWindow, clipNative, IsMagenta);
        var translatedLabelText = (labelNative as NSTextField)?.StringValue;
        // Keep the zoom proof fully inside the clip; clipped glyph area need not grow with scale.
        _movedLabel.Text = "Zoom";
        var zoomBaseline = await CaptureAsync(context, nativeWindow, "zoom-base.png");
        var zoomBaselineMagentaTextPixels = CountPixels(zoomBaseline, nativeWindow, clipNative, IsMagenta);
        _world.Scale = 1.1;
        var firstZoom = await CaptureAsync(context, nativeWindow, "zoomed-1.png");
        var firstZoomMagentaTextPixels = CountPixels(firstZoom, nativeWindow, clipNative, IsMagenta);
        var firstZoomMagentaPixelsOutsideClip = CountPixels(firstZoom, nativeWindow, windowContent, IsMagenta)
            - firstZoomMagentaTextPixels;
        _world.Scale = 1.25;
        var zoomed = await CaptureAsync(context, nativeWindow, "zoomed.png");
        var zoomedMagentaTextPixels = CountPixels(zoomed, nativeWindow, clipNative, IsMagenta);
        var zoomedMagentaPixelsOutsideClip = CountPixels(zoomed, nativeWindow, windowContent, IsMagenta)
            - zoomedMagentaTextPixels;

        context.WriteJson("native-state.json", new
        {
            clips,
            unclipped,
            clipsAgain,
            outsideClipIsLavender,
            translatedOutsideClipIsLavender,
            magentaTextPixels,
            magentaPixelsOutsideClip,
            zoomBaselineMagentaTextPixels,
            firstZoomMagentaTextPixels,
            firstZoomMagentaPixelsOutsideClip,
            zoomedMagentaTextPixels,
            zoomedMagentaPixelsOutsideClip,
            controlPixels,
            initialOutsidePixels,
            disabledOutsidePixels,
            restoredOutsidePixels,
            bitmap = new { initial.Width, initial.Height },
            windowFrame = nativeWindow.Frame.ToString(),
            clipFrame = clipNative.Frame.ToString(),
            worldFrame = worldNative.Frame.ToString(),
            labelFrame = labelNative.Frame.ToString(),
            labelText = translatedLabelText,
            zoomLabelText = (labelNative as NSTextField)?.StringValue,
        });

        if (!clips && !clipsAgain && outsideClipIsLavender &&
            magentaTextPixels == 0 && zoomedMagentaTextPixels == 0)
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
        context.Assert(disabledOutsidePixels > 5, "Turning clipping off did not restore outside paint.",
            "contentview.rendered-unclipped");
        context.Assert(restoredOutsidePixels == 0, "Restoring clipping left paint outside its bounds.",
            "contentview.rendered-restored");
        context.Pass("IsClippedToBounds updates the native layer");

        context.Assert(translatedLabelText == "Moved into view",
            "The moved native label lost its text.", "contentview.label-text");
        context.Assert(worldNative.Frame.X == -600,
            $"Expected translated native frame, got {worldNative.Frame}.",
            "contentview.translation");
        context.Assert(magentaTextPixels > 5,
            $"Text moved into view did not render; found {magentaTextPixels} magenta text pixels.",
            "contentview.rendered-text");
        context.Assert(!translatedOutsideClipIsLavender && magentaPixelsOutsideClip == 0,
            $"Translated content escaped the clip: lavender={translatedOutsideClipIsLavender}, " +
            $"outside text pixels={magentaPixelsOutsideClip}.",
            "contentview.translated-clip");
        context.Pass("Transformed content and text redraw after moving into view");
        context.Assert((labelNative as NSTextField)?.StringValue == "Zoom" &&
            zoomBaselineMagentaTextPixels > 5 &&
            firstZoomMagentaTextPixels > zoomBaselineMagentaTextPixels &&
            zoomedMagentaTextPixels > firstZoomMagentaTextPixels,
            $"Text did not visibly scale across repeated zoom updates; pixel counts were " +
            $"{zoomBaselineMagentaTextPixels}, {firstZoomMagentaTextPixels}, and {zoomedMagentaTextPixels}.",
            "contentview.zoomed-text");
        context.Assert(firstZoomMagentaPixelsOutsideClip == 0 && zoomedMagentaPixelsOutsideClip == 0,
            $"Zoomed text escaped the clip by {firstZoomMagentaPixelsOutsideClip} and " +
            $"{zoomedMagentaPixelsOutsideClip} pixels.",
            "contentview.zoomed-clip");
        context.Pass("Moved content remains rendered and clipped after zoom");
        _world.Scale = 1;

        var glyphCounts = new int[2];
        for (var cycle = 0; cycle < 10; cycle++)
        {
            _world.TranslationX = 0;
            var outside = await CaptureAsync(context, nativeWindow, $"outside-{cycle}.png");
            context.Assert(CountPixels(outside, nativeWindow, windowContent, IsMagenta, expectedLabelRegion) == 0,
                $"Cycle {cycle}: translation to zero left stale text.", "contentview.zero-text");
            context.Assert(worldNative.Frame.X == 0, $"Cycle {cycle}: translation did not reset the native frame.",
                "contentview.zero-frame");
            _movedLabel.Text = cycle % 2 == 0 ? "Fresh text even" : "Fresh text odd";
            _world.TranslationX = -600;
            var returned = await CaptureAsync(context, nativeWindow, $"returned-{cycle}.png");
            var pixels = CountPixels(returned, nativeWindow, windowContent, IsMagenta, expectedLabelRegion);
            context.AppendJson("transforms.jsonl", new { cycle, pixels, frame = worldNative.Frame.ToString() });
            context.Assert(pixels > 5, $"Cycle {cycle}: moved text is blank ({pixels} glyph pixels).",
                "contentview.repeated-text");
            context.Assert(worldNative.Frame.X == -600, $"Cycle {cycle}: translation did not survive layout.",
                "contentview.repeated-frame");
            if (cycle < 2)
                glyphCounts[cycle] = pixels;
            context.Assert(cycle == 0 || (glyphCounts[0] != glyphCounts[1] && pixels == glyphCounts[cycle % 2]),
                $"Cycle {cycle}: alternating text did not restore its distinct raster ({pixels} pixels).",
                "contentview.fresh-text");
        }
        context.Pass("Repeated translation and reset render fresh text");

        var nestedLabel = new Label { Text = "Nested label", TextColor = Colors.Orange, Padding = 4 };
        var entry = new Entry { Text = "Entry text", TextColor = Colors.Blue, FontSize = 14 };
        var nested = new ContentView
        {
            IsClippedToBounds = true,
            Content = new ContentView
            {
                IsClippedToBounds = true,
                Content = new Grid { Children = { nestedLabel } },
            },
        };
        AbsoluteLayout.SetLayoutBounds(nested, new Rect(700, 5, 160, 42));
        AbsoluteLayout.SetLayoutBounds(entry, new Rect(700, 118, 160, 32));
        _world.Children.Add(nested);
        _world.Children.Add(entry);
        _world.TranslationX = 0;
        await CaptureAsync(context, nativeWindow, "nested-outside.png");
        _world.TranslationX = -600;
        var nestedImage = await CaptureAsync(context, nativeWindow, "nested.png");
        var nestedPixels = CountPixels(nestedImage, nativeWindow, Native(nestedLabel), IsOrange);
        var entryPixels = CountPixels(nestedImage, nativeWindow, Native(entry), IsBlue);
        context.Assert(nestedPixels > 5, $"Nested ContentView/Grid/Label has {nestedPixels} glyph pixels.",
            "contentview.nested-label");
        context.Assert(entryPixels > 5, $"Translated Entry has {entryPixels} glyph pixels.",
            "contentview.entry");
        context.Assert(Native(nested).Layer?.MasksToBounds == true,
            "Nested ContentView lost its clip.", "contentview.nested-clip");
        context.Pass("Nested ContentViews, layout, Label and Entry render");

        foreach (var children in new[] { 0, 64 })
        {
            for (var i = 0; i < children; i++)
            {
                var label = new Label { Text = $"Load {i}" };
                AbsoluteLayout.SetLayoutBounds(label, new Rect(900, i * 20, 100, 20));
                _world.Children.Add(label);
            }
            await CaptureAsync(context, nativeWindow, $"load-{children}.png");
            foreach (var updates in new[] { 10, 100, 500 })
            {
                var drawsBefore = ObservedTextField.Draws;
                var timer = Stopwatch.StartNew();
                for (var i = 0; i < updates; i++)
                {
                    _world.TranslationX = i % 2 == 0 ? -590 : -600;
                    _world.TranslationY = i % 2 == 0 ? 1 : 0;
                    _world.Scale = i % 2 == 0 ? 1.01 : 1;
                }
                timer.Stop();
                var synchronousDraws = ObservedTextField.Draws - drawsBefore;
                var image = await CaptureAsync(context, nativeWindow, $"load-{children}-{updates}.png");
                var pixels = CountPixels(image, nativeWindow, Native(_movedLabel), IsMagenta);
                context.AppendJson("redraw-cost.jsonl", new
                {
                    children, updates, mapperCalls = updates * 3, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
                    synchronousDraws, settledDraws = ObservedTextField.Draws - drawsBefore,
                    ObservedTextField.MaximumDepth, ObservedTextField.OffMainThreadDraws, pixels,
                    nestedPixels = CountPixels(image, nativeWindow, Native(nestedLabel), IsOrange),
                    entryPixels = CountPixels(image, nativeWindow, Native(entry), IsBlue),
                });
                context.Assert(synchronousDraws == 0,
                    $"Transform batch synchronously drew {synchronousDraws} labels.", "contentview.synchronous-redraw");
                context.Assert(ObservedTextField.MaximumDepth == 1, "Label drawing was reentrant.",
                    "contentview.reentrant-redraw");
                context.Assert(ObservedTextField.OffMainThreadDraws == 0, "Label drawing left the UI thread.",
                    "contentview.redraw-thread");
                context.Assert(pixels > 5, $"Batch lost moved text ({pixels} pixels).", "contentview.batch-text");
                context.Assert(CountPixels(image, nativeWindow, Native(nestedLabel), IsOrange) > 5,
                    "Batch lost nested label text.", "contentview.batch-nested");
                context.Assert(CountPixels(image, nativeWindow, Native(entry), IsBlue) > 5,
                    "Batch lost Entry text.", "contentview.batch-entry");
            }
        }
        context.Pass("Transform batches avoid synchronous recursive redraw");

        Label? itemLabel = null;
        var collection = new CollectionView
        {
            ItemsSource = new[] { "First item", "Second item" },
            ItemTemplate = new DataTemplate(() => itemLabel = new Label
            {
                Text = "Native item", TextColor = Colors.Blue,
            }),
        };
        AbsoluteLayout.SetLayoutBounds(collection, new Rect(320, 210, 240, 80));
        ((AbsoluteLayout)_clip.Parent).Children.Add(collection);
        await CaptureAsync(context, nativeWindow, "collection-layout.png");
        collection.ItemsSource = new[] { "Rebound first", "Rebound second" };
        await CaptureAsync(context, nativeWindow, "collection-before.png");
        var itemNative = Native(itemLabel ?? throw new InvalidOperationException("CollectionView did not create its item."));
        var itemFrameBefore = itemNative.Frame;
        itemLabel.Scale = 1.01;
        await CaptureAsync(context, nativeWindow, "collection-after.png");
        var itemFrameAfter = itemNative.Frame;
        context.WriteJson("collection-state.json", new
        {
            before = itemFrameBefore.ToString(), after = itemFrameAfter.ToString(),
            managedFrame = ((IView)itemLabel).Frame.ToString(),
        });
        context.Assert(itemFrameAfter == itemFrameBefore,
            $"Changing a native-positioned CollectionView item's scale reset its frame from {itemFrameBefore} to {itemFrameAfter}.",
            "contentview.native-positioned-transform");
        context.Pass("Transform preserves a native-positioned CollectionView item frame");

        _world.TranslationX = 0;
        _world.AnchorX = 0;
        _world.AnchorY = 0;
        var scaledLabel = new Label { Text = "Scaled text", TextColor = Colors.Magenta, FontSize = 40 };
        AbsoluteLayout.SetLayoutBounds(scaledLabel, new Rect(900, 40, 260, 80));
        _world.Children.Add(scaledLabel);
        await CaptureAsync(context, nativeWindow, "scale-outside.png");
        _world.Scale = 0.2;
        var scaled = await CaptureAsync(context, nativeWindow, "scaled.png");
        var scaledRegion = new CGRect(clipNative.Frame.X + 180, clipNative.Frame.Y + 8, 52, 16);
        var scaledPixels = CountPixels(scaled, nativeWindow, windowContent, IsMagenta, scaledRegion);
        context.WriteJson("scale-state.json", new
        {
            scaledPixels, labelFrame = Native(scaledLabel).Frame.ToString(),
            worldFrame = worldNative.Frame.ToString(), transform = worldNative.Layer?.Transform.ToString(),
        });
        context.Assert(scaledPixels > 5, $"Scale brought initially off-window text into view with {scaledPixels} glyph pixels.",
            "contentview.scaled-text");
        context.Pass("Scale brings initially off-window text into view");
    }

    async Task<RuntimeBitmap> CaptureAsync(RuntimeTestContext context, NSWindow window, string name)
    {
        var timer = Stopwatch.StartNew();
        var attempts = 0;
        while (true)
        {
            await RuntimeTestContext.FlushMainQueueAsync();
            window.ContentView!.LayoutSubtreeIfNeeded();
            CATransaction.Flush();
            await RuntimeTestContext.FlushMainQueueAsync();
            if (!NSThread.IsMain)
                throw new InvalidOperationException("Compositor capture left AppKit's main thread.");
            var image = context.CaptureWindowBitmap(window, name);
            var controlPixels = CountPixels(image, window, Native(_controlLabel), IsCyan);
            attempts++;
            var fullSize = (image.Width == (int)window.Frame.Width && image.Height == (int)window.Frame.Height)
                || (image.Width == (int)(window.Frame.Width * window.BackingScaleFactor)
                    && image.Height == (int)(window.Frame.Height * window.BackingScaleFactor));
            if ((controlPixels > 5 && fullSize) || timer.Elapsed.TotalSeconds >= 5)
            {
                context.AppendJson("capture-state.jsonl", new
                {
                    name, attempts, controlPixels, window.IsVisible, window.IsMiniaturized,
                    windowNumber = (long)window.WindowNumber,
                    backingScale = (double)window.BackingScaleFactor,
                    windowFrame = window.Frame.ToString(), controlFrame = Native(_controlLabel).Frame.ToString(),
                    controlInWindow = Native(_controlLabel).ConvertRectToView(Native(_controlLabel).Bounds, null).ToString(),
                    ObservedTextField.Draws, image.Width, image.Height,
                });
                if (controlPixels <= 5 || !fullSize)
                    throw new InvalidOperationException($"Compositor capture '{name}' did not show the full-size window and independent text control.");
                return image;
            }
        }
    }

    static bool IsLavender(byte first, byte green, byte third) =>
        first >= 225 && green is >= 220 and <= 240 && third >= 225
        && Math.Max(first, third) - Math.Min(first, third) >= 10;

    static bool IsMagenta(byte red, byte green, byte blue) => red > 150 && green < 140 && blue > 150;
    static bool IsCyan(byte red, byte green, byte blue) => red < 140 && green > 150 && blue > 150;
    static bool IsOrange(byte red, byte green, byte blue) => red > 180 && green is > 60 and < 200 && blue < 100;
    static bool IsBlue(byte red, byte green, byte blue) => red < 140 && green < 140 && blue > 150;

    static int CountPixels(RuntimeBitmap image, NSWindow window, NSView view,
        Func<byte, byte, byte, bool> matches, CGRect? region = null)
    {
        if (image.SamplesPerPixel < 3)
            throw new InvalidOperationException("The compositor bitmap is not RGB.");
        var bounds = view.ConvertRectToView(region ?? view.Bounds, null);
        var scaleX = image.Width / window.Frame.Width;
        var scaleY = image.Height / window.Frame.Height;
        var left = Math.Clamp((int)(bounds.X * scaleX), 0, image.Width);
        var right = Math.Clamp((int)(bounds.Right * scaleX), 0, image.Width);
        var top = Math.Clamp((int)((window.Frame.Height - bounds.Bottom) * scaleY), 0, image.Height);
        var bottom = Math.Clamp((int)((window.Frame.Height - bounds.Y) * scaleY), 0, image.Height);
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = y * image.BytesPerRow + x * image.SamplesPerPixel;
                if (matches(image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2]))
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

sealed class ObservedLabelHandler : LabelHandler
{
    protected override MauiNSTextField CreatePlatformView() => new ObservedTextField
    {
        Editable = false, Selectable = false, Bordered = false, DrawsBackground = false,
        TextColor = NSColor.ControlText, MaximumNumberOfLines = 0,
    };
}

sealed class ObservedTextField : MauiNSTextField
{
    static int _depth;
    public static int Draws { get; private set; }
    public static int MaximumDepth { get; private set; }
    public static int OffMainThreadDraws { get; private set; }

    public override void DrawRect(CGRect dirtyRect)
    {
        Draws++;
        if (!NSThread.IsMain)
            OffMainThreadDraws++;
        MaximumDepth = Math.Max(MaximumDepth, ++_depth);
        try
        {
            base.DrawRect(dirtyRect);
        }
        finally
        {
            _depth--;
        }
    }
}
