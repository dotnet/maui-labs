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
        ScenarioRegistry.Register(new("contentview-clipping", 15,
            context => new ContentViewClippingScenario().CreateDelegate(context),
            ExpectedAssertions: 184));
}

sealed class ContentViewClippingScenario : MauiRuntimeScenario
{
    ContentView _clip = null!;
    AbsoluteLayout _world = null!;
    Label _movedLabel = null!;
    Label _controlLabel = null!;

    public override void Configure(MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Label, ObservedLabelHandler>();
            handlers.AddHandler<ContentView, ObservedContentViewHandler>();
        });

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
        var translatedWorldFrame = worldNative.Frame;
        // Keep the zoom proof fully inside the clip; clipped glyph area need not grow with scale.
        _movedLabel.Text = "Zoom";
        var zoomBaseline = await CaptureAsync(context, nativeWindow, "zoom-base.png");
        var zoomBaselineMagentaTextPixels = CountPixels(zoomBaseline, nativeWindow, clipNative, IsMagenta);
        _world.Scale = 1.1;
        var (firstZoom, firstZoomMagentaTextPixels, firstZoomMagentaPixelsOutsideClip,
            firstZoomChangedPixels) = await CaptureChangedCompositorFrame(
                context, nativeWindow, "zoomed-1.png", zoomBaseline, windowContent, clipNative);

        _world.Scale = 1.25;
        var (_, zoomedMagentaTextPixels, zoomedMagentaPixelsOutsideClip,
            zoomedChangedPixels) = await CaptureChangedCompositorFrame(
                context, nativeWindow, "zoomed.png", firstZoom, windowContent, clipNative);

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
            firstZoomChangedPixels,
            zoomedMagentaTextPixels,
            zoomedMagentaPixelsOutsideClip,
            zoomedChangedPixels,
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
        context.Assert(translatedWorldFrame.X == -600,
            $"Expected translated native frame, got {translatedWorldFrame}.",
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
            firstZoomChangedPixels > 5 && zoomedChangedPixels > 5 &&
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
        _world.AnchorX = _world.AnchorY = 0;
        _world.Scale = 0.9;
        var nestedScaled = await CaptureAsync(context, nativeWindow, "nested-scaled.png");
        context.Assert(CountPixels(nestedScaled, nativeWindow, Native(nestedLabel), IsOrange) > 5,
            "Scaling lost nested label text.", "contentview.scaled-nested");
        context.Assert(CountPixels(nestedScaled, nativeWindow, Native(entry), IsBlue) > 5,
            "Scaling lost Entry text.", "contentview.scaled-entry");
        context.Pass("Native scaling preserves nested Label and Entry rendering");
        _world.Scale = 1;
        _world.AnchorX = _world.AnchorY = 0.5;

        entry.Scale = 1.1;
        entry.TranslationX = 7;
        await CaptureAsync(context, nativeWindow, "entry-scaled.png");
        var entryFrame = Native(entry).Frame;
        var entryBounds = Native(entry).Bounds;
        entry.IsPassword = true;
        await CaptureAsync(context, nativeWindow, "entry-secure.png");
        context.Assert(Native(entry) is NSSecureTextField && FramesMatch(Native(entry).Frame, entryFrame) &&
            FramesMatch(Native(entry).Bounds, entryBounds),
            "Secure Entry replacement lost transformed geometry.", "contentview.secure-entry-geometry");
        entry.IsPassword = false;
        var plainEntry = await CaptureAsync(context, nativeWindow, "entry-plain.png");
        context.Assert(Native(entry) is not NSSecureTextField && FramesMatch(Native(entry).Frame, entryFrame) &&
            FramesMatch(Native(entry).Bounds, entryBounds),
            "Plain Entry replacement lost transformed geometry.", "contentview.plain-entry-geometry");
        context.Assert(CountPixels(plainEntry, nativeWindow, Native(entry), IsBlue) > 5,
            "Replaced transformed Entry lost text or its text style.", "contentview.replaced-entry-text");
        entry.Scale = 1;
        entry.TranslationX = 0;
        context.Pass("Entry replacement preserves transformed geometry and text");

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
        var expectedItemFrame = new CGRect(
            itemFrameBefore.X - itemFrameBefore.Width * 0.005,
            itemFrameBefore.Y - itemFrameBefore.Height * 0.005,
            itemFrameBefore.Width * 1.01, itemFrameBefore.Height * 1.01);
        context.Assert(FramesMatch(itemFrameAfter, expectedItemFrame),
            $"Expected anchored native scale frame {expectedItemFrame}, got {itemFrameAfter}.",
            "contentview.native-positioned-transform");
        context.Assert(itemNative.Bounds.Size == itemFrameBefore.Size,
            "Scaling changed the item's logical layout size.", "contentview.item-bounds");
        itemLabel.Scale = 1;
        await CaptureAsync(context, nativeWindow, "collection-reset.png");
        context.Assert(FramesMatch(itemNative.Frame, itemFrameBefore),
            "Resetting scale did not restore the native-owned item frame.", "contentview.item-reset");
        itemLabel.TranslationX = 10;
        itemLabel.TranslationY = 7;
        await CaptureAsync(context, nativeWindow, "collection-translated.png");
        context.Assert(FramesMatch(itemNative.Frame, new CGRect(
            itemFrameBefore.X + 10, itemFrameBefore.Y + 7, itemFrameBefore.Width, itemFrameBefore.Height)),
            "Translation without scale lost the item's native origin.", "contentview.item-translation");
        itemLabel.TranslationX = itemLabel.TranslationY = 0;
        await CaptureAsync(context, nativeWindow, "collection-zero.png");
        context.Assert(FramesMatch(itemNative.Frame, itemFrameBefore),
            "Translation reset lost the item's native origin.", "contentview.item-zero");
        itemLabel.Handler!.PlatformArrange(new Rect(20, 40, 240, 16));
        itemLabel.Scale = 1.1;
        await CaptureAsync(context, nativeWindow, "collection-relayout.png");
        context.Assert(FramesMatch(itemNative.Frame, new CGRect(8, 39.2, 264, 17.6)),
            "Scale did not preserve the native owner's logical frame.", "contentview.item-relayout");
        collection.Handler!.PlatformArrange(new Rect(320, 210, 240, 80));
        await CaptureAsync(context, nativeWindow, "collection-owner-relayout.png");
        context.Assert(itemLabel.Scale == 1.1 && FramesMatch(itemNative.Frame, new CGRect(-12, 15.2, 264, 17.6)),
            "Native owner relayout lost an unchanged item transform.", "contentview.item-owner-relayout");
        itemLabel.Handler.PlatformArrange(new Rect(20, 40, 240, 16));
        itemLabel.Scale = 1;
        await CaptureAsync(context, nativeWindow, "collection-relayout-reset.png");
        context.Assert(FramesMatch(itemNative.Frame, new CGRect(20, 40, 240, 16)),
            "Resetting scale did not preserve the new native frame.", "contentview.item-relayout-reset");
        collection.Scale = 0.5;
        collection.ItemsSource = new[] { "Scaled first", "Scaled second" };
        var scaledCollection = await CaptureAsync(context, nativeWindow, "collection-scaled.png");
        var scaledItem = Native(itemLabel);
        context.Assert(scaledItem.Bounds.Width == 240 && Native(collection).Bounds.Width == 240,
            "Scaled CollectionView used physical frame width for logical item layout.", "contentview.collection-logical-size");
        context.Assert(CountPixels(scaledCollection, nativeWindow, scaledItem, IsBlue) > 5,
            "Scaled CollectionView lost its rebound item's text.", "contentview.collection-scaled-text");
        collection.Scale = 1;
        context.Pass("Transform preserves a native-positioned CollectionView item frame");

        var compositeItems = new List<ContentView>();
        collection.ItemsSource = null;
        collection.ItemTemplate = new DataTemplate(() =>
        {
            var item = new ContentView
            {
                HeightRequest = 48,
                Content = new Grid
                {
                    Children = { new Label { Text = "Composite item", TextColor = Colors.Blue } },
                },
            };
            compositeItems.Add(item);
            return item;
        });
        collection.ItemsSource = new[] { "Composite first", "Composite second" };
        await CaptureAsync(context, nativeWindow, "collection-composite-before.png");
        context.Assert(compositeItems.Count == 2,
            $"Expected two composite item roots, got {compositeItems.Count}.", "contentview.composite-items");
        var compositeFrames = compositeItems.Select(item => Native(item).Frame).ToArray();
        var compositeHandlers = compositeItems.Select(item => (ObservedContentViewHandler)item.Handler!).ToArray();
        var arrangedBeforeScroll = compositeHandlers.Sum(handler => handler.ArrangeCalls);
        var scrollView = (NSScrollView)Native(collection);
        var notifications = 0;
        using (var observer = NSNotificationCenter.DefaultCenter.AddObserver(
            NSView.BoundsChangedNotification, _ => notifications++, scrollView.ContentView))
        {
            for (var step = 1; step <= 10; step++)
                scrollView.ContentView.ScrollToPoint(new CGPoint(0, step * 0.5));
        }
        context.Assert(notifications > 0, "No native scroll notifications were exercised.",
            "contentview.composite-scroll-notifications");
        var arrangedAfterScroll = compositeHandlers.Sum(handler => handler.ArrangeCalls);
        context.Assert(arrangedAfterScroll == arrangedBeforeScroll &&
            compositeItems.Select((item, index) => FramesMatch(Native(item).Frame, compositeFrames[index])).All(match => match),
            $"Unchanged composite items were arranged {arrangedAfterScroll - arrangedBeforeScroll} times while scrolling.",
            "contentview.composite-scroll-layout");
        var compositeScrolled = await CaptureAsync(context, nativeWindow, "collection-composite-scrolled.png");
        context.Assert(CountPixels(compositeScrolled, nativeWindow, Native(collection), IsBlue) > 5,
            "Scrolling lost composite item text.", "contentview.composite-scroll-text");
        context.WriteJson("scroll-cost.json", new { notifications, arrangedBeforeScroll, arrangedAfterScroll });
        collection.ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)
        {
            HorizontalItemSpacing = 10,
        };
        var gridFrames = compositeItems.Select(item => Native(item).Frame).OrderBy(frame => frame.X).ToArray();
        var columnWidth = (Native(collection).Bounds.Width - 10) / 2;
        context.Assert(compositeHandlers.Sum(handler => handler.ArrangeCalls) > arrangedAfterScroll &&
            Math.Abs(gridFrames[0].Width - columnWidth) < 0.0001 &&
            Math.Abs(gridFrames[1].Width - columnWidth) < 0.0001 &&
            Math.Abs(gridFrames[0].X) < 0.0001 && Math.Abs(gridFrames[1].X - columnWidth - 10) < 0.0001,
            "Changing ItemsLayout did not reposition existing composite roots.", "contentview.composite-layout-change");
        var compositeGrid = await CaptureAsync(context, nativeWindow, "collection-composite-grid.png");
        context.Assert(CountPixels(compositeGrid, nativeWindow, Native(collection), IsBlue) > 5,
            "Changing ItemsLayout lost composite text.", "contentview.composite-layout-text");
        context.Pass("Scrolling does not rearrange unchanged composite item roots");

        var toolbarLabel = new Label { Text = "Toolbar content", TextColor = Colors.Blue };
        toolbarLabel.ToHandler(_clip.Handler!.MauiContext!);
        toolbarLabel.Arrange(new Rect(0, 0, 160, 28));
        var toolbarNative = Native(toolbarLabel);
        using (var toolbarButton = new NSButton(new CGRect(320, 170, 160, 28)))
        {
            toolbarButton.AddSubview(toolbarNative);
            toolbarNative.TranslatesAutoresizingMaskIntoConstraints = false;
            toolbarNative.LeadingAnchor.ConstraintEqualTo(toolbarButton.LeadingAnchor).Active = true;
            toolbarNative.TrailingAnchor.ConstraintEqualTo(toolbarButton.TrailingAnchor).Active = true;
            toolbarNative.CenterYAnchor.ConstraintEqualTo(toolbarButton.CenterYAnchor).Active = true;
            toolbarNative.HeightAnchor.ConstraintEqualTo(28).Active = true;
            using var toolbarItem = new NSToolbarItem("regression-content") { View = toolbarButton };
            using var toolbarDelegate = new ObservedToolbarDelegate(toolbarItem);
            using var toolbar = new NSToolbar("regression-toolbar")
            {
                Delegate = toolbarDelegate,
                DisplayMode = NSToolbarDisplayMode.Icon,
            };
            var previousToolbar = nativeWindow.Toolbar;
            nativeWindow.Toolbar = toolbar;
            toolbar.Visible = true;
            try
            {
                foreach (var width in new[] { 160, 240 })
                {
                    toolbarItem.MinSize = toolbarItem.MaxSize = new CGSize(width, 28);
                    toolbarButton.Frame = new CGRect(0, 0, width, 28);
                    toolbarButton.NeedsLayout = true;
                    toolbarButton.LayoutSubtreeIfNeeded();
                    await CaptureAsync(context, nativeWindow, $"toolbar-resize-{width}.png");
                    var resolvedFrame = toolbarNative.Frame;
                    var resolvedBounds = toolbarNative.Bounds;
                    var alignmentInsets = toolbarNative.AlignmentRectInsets;
                    context.AppendJson("toolbar-state.jsonl", new
                    {
                        width, button = toolbarButton.Frame.ToString(),
                        label = resolvedFrame.ToString(), bounds = resolvedBounds.ToString(),
                        alignmentInsets = alignmentInsets.ToString(),
                    });
                    context.Assert(Math.Abs(resolvedFrame.Width - alignmentInsets.Left - alignmentInsets.Right - width) < 0.0001 &&
                        Math.Abs(resolvedFrame.X + alignmentInsets.Left) < 0.0001,
                        $"Toolbar Auto Layout expected alignment width {width}, got frame {resolvedFrame}, insets {alignmentInsets}.",
                        "contentview.toolbar-layout");
                    toolbarLabel.Scale = width == 160 ? 0.9 : 0.8;
                    toolbarLabel.AnchorX = width == 160 ? 0.25 : 0.75;
                    toolbarLabel.TranslationX = width == 160 ? 8 : 12;
                    toolbarLabel.Handler!.PlatformArrange(new Rect(0, 0, 160, 28));
                    var toolbarImage = await CaptureAsync(context, nativeWindow, $"toolbar-transformed-{width}.png");
                    context.Assert(FramesMatch(toolbarNative.Frame, resolvedFrame) &&
                        FramesMatch(toolbarNative.Bounds, resolvedBounds),
                        "A transform overwrote the toolbar's Auto Layout geometry.", "contentview.toolbar-transform");
                    context.Assert(CountPixels(toolbarImage, nativeWindow, toolbarButton, IsBlue) > 5,
                        "Transformed Auto Layout toolbar text is blank.", "contentview.toolbar-text");
                }
            }
            finally
            {
                toolbar.Visible = false;
                nativeWindow.Toolbar = previousToolbar;
                toolbarNative.RemoveFromSuperview();
            }
        }
        toolbarLabel.Handler!.DisconnectHandler();
        context.Pass("Auto Layout toolbar content retains native geometry across transforms");

        _world.TranslationX = 0;
        _world.AnchorX = 0;
        _world.AnchorY = 0;
        var scaledLabel = new Label { Text = "Scaled text", TextColor = Colors.Magenta, FontSize = 40 };
        AbsoluteLayout.SetLayoutBounds(scaledLabel, new Rect(900, 40, 260, 80));
        _world.Children.Add(scaledLabel);
        var scaleMarker = new BoxView { Color = Colors.Lime };
        AbsoluteLayout.SetLayoutBounds(scaleMarker, new Rect(900, 140, 200, 40));
        _world.Children.Add(scaleMarker);
        using var autoresizedChild = new NSView(new CGRect(800, 100, 80, 20))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        worldNative.AddSubview(autoresizedChild);
        await CaptureAsync(context, nativeWindow, "scale-outside.png");
        _world.Scale = 0.2;
        var scaled = await CaptureAsync(context, nativeWindow, "scaled.png");
        var scaledRegion = new CGRect(clipNative.Frame.X + 180, clipNative.Frame.Y + 8, 52, 16);
        var scaledPixels = CountPixels(scaled, nativeWindow, windowContent, IsMagenta, scaledRegion);
        var markerRegion = new CGRect(clipNative.Frame.X + 180, clipNative.Frame.Y + 28, 40, 8);
        var markerPixels = CountPixels(scaled, nativeWindow, windowContent, IsLime, markerRegion);
        var captureScale = scaled.Width / (double)nativeWindow.Frame.Width;
        context.WriteJson("scale-state.json", new
        {
            scaledPixels, markerPixels, captureScale, labelFrame = Native(scaledLabel).Frame.ToString(),
            worldFrame = worldNative.Frame.ToString(), transform = worldNative.Layer?.Transform.ToString(),
        });
        context.Assert(scaledPixels > 5, $"Scale brought initially off-window text into view with {scaledPixels} glyph pixels.",
            "contentview.scaled-text");
        context.Assert(markerPixels == 320 * captureScale * captureScale,
            $"Expected a rendered 40x8-point scale marker, found {markerPixels} pixels.",
            "contentview.scale-rendered-extent");
        context.Assert(CountPixels(scaled, nativeWindow, windowContent, IsLime) == markerPixels,
            "Scale marker rendered outside its expected single-scale region.", "contentview.scale-marker-region");
        context.Pass("Scale brings initially off-window text into view");
        var visible = Native(scaledLabel).VisibleRect();
        context.Assert(visible.Width > 0 && visible.Height > 0 && visible.Right > 0 && visible.Bottom > 0 &&
            visible.X < 260 && visible.Y < 80,
            $"AppKit still considers scaled text invisible: {visible}.", "contentview.native-scale-visibility");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            _world.Scale = 1;
            var reset = await CaptureAsync(context, nativeWindow, $"scale-reset-{cycle}.png");
            context.Assert(CountPixels(reset, nativeWindow, windowContent, IsMagenta, scaledRegion) == 0,
                "Resetting scale left stale glyphs.", "contentview.scale-zero-text");
            _world.Scale = 0.2;
            var returned = await CaptureAsync(context, nativeWindow, $"scale-returned-{cycle}.png");
            context.Assert(CountPixels(returned, nativeWindow, windowContent, IsMagenta, scaledRegion) == scaledPixels,
                "Repeated scale exposure lost or changed text.", "contentview.scale-returned-text");
            context.Assert(FramesMatch(worldNative.Frame, new CGRect(0, 0, 240, 48)) &&
                FramesMatch(worldNative.Bounds, new CGRect(0, 0, 1200, 240)),
                "Repeated scale changed logical bounds or native frame.", "contentview.scale-geometry");
            context.Assert(worldNative.AutoresizesSubviews &&
                FramesMatch(autoresizedChild.Frame, new CGRect(800, 100, 80, 20)),
                $"Transform geometry resized an autoresizing child ({autoresizedChild.Frame}) or changed its owner's flag ({worldNative.AutoresizesSubviews}).",
                "contentview.autoresizing-child");
        }
        _world.AnchorX = _world.AnchorY = 0.5;
        var anchored = await CaptureAsync(context, nativeWindow, "scale-anchored.png");
        context.Assert(FramesMatch(worldNative.Frame, new CGRect(480, 96, 240, 48)),
            "Scale did not honor the center anchor.", "contentview.scale-anchor-frame");
        context.Assert(CountPixels(anchored, nativeWindow, windowContent, IsMagenta, scaledRegion) == 0,
            "Changing the anchor left stale text.", "contentview.scale-anchor-text");
        _world.AnchorX = _world.AnchorY = 0;
        var unanchored = await CaptureAsync(context, nativeWindow, "scale-unanchored.png");
        context.Assert(CountPixels(unanchored, nativeWindow, windowContent, IsMagenta, scaledRegion) == scaledPixels,
            "Resetting the anchor did not restore readable text.", "contentview.scale-anchor-reset");
        context.Pass("Repeated scale and anchor resets preserve native visibility");

        _world.Scale = 1;
        _world.ScaleX = 0.2;
        _world.ScaleY = 0.25;
        var axes = await CaptureAsync(context, nativeWindow, "scale-axes.png");
        var axesRegion = new CGRect(clipNative.Frame.X + 180, clipNative.Frame.Y + 10, 52, 20);
        context.Assert(CountPixels(axes, nativeWindow, windowContent, IsMagenta, axesRegion) > 5,
            "Nonuniform scale lost readable text.", "contentview.scale-axes-text");
        context.Assert(FramesMatch(worldNative.Frame, new CGRect(0, 0, 240, 60)),
            "Nonuniform scale produced the wrong native frame.", "contentview.scale-axes-frame");
        context.Assert(FramesMatch(worldNative.Bounds, new CGRect(0, 0, 1200, 240)),
            "Nonuniform scale changed logical layout bounds.", "contentview.scale-axes-bounds");
        _world.ScaleX = _world.ScaleY = 1;
        var axesReset = await CaptureAsync(context, nativeWindow, "scale-axes-reset.png");
        context.Assert(FramesMatch(worldNative.Frame, new CGRect(0, 0, 1200, 240)),
            "Axis reset did not restore the layout frame.", "contentview.scale-axes-reset-frame");
        context.Assert(CountPixels(axesReset, nativeWindow, windowContent, IsMagenta, axesRegion) == 0,
            "Axis reset left stale glyphs.", "contentview.scale-axes-reset-text");
        context.Pass("Nonuniform scale preserves native geometry and text");
        for (var cycle = 0; cycle < 10; cycle++)
        {
            var scale = cycle % 2 == 0 ? 0.19 : 0.2;
            var drawsBefore = ObservedTextField.Draws;
            var timer = Stopwatch.StartNew();
            _world.Scale = scale;
            timer.Stop();
            var synchronousDraws = ObservedTextField.Draws - drawsBefore;
            var image = await CaptureAsync(context, nativeWindow, $"paced-{cycle}.png");
            var region = new CGRect(clipNative.Frame.X + 900 * scale, clipNative.Frame.Y + 40 * scale,
                260 * scale, 80 * scale);
            var pixels = CountPixels(image, nativeWindow, windowContent, IsMagenta, region);
            context.AppendJson("paced-cost.jsonl", new
            {
                cycle, scale, updateMilliseconds = timer.Elapsed.TotalMilliseconds, synchronousDraws,
                settledDraws = ObservedTextField.Draws - drawsBefore,
                ObservedTextField.MaximumDepth, ObservedTextField.OffMainThreadDraws, pixels,
            });
            context.Assert(synchronousDraws == 0, "Paced scaling synchronously drew labels.",
                "contentview.paced-synchronous-redraw");
            context.Assert(ObservedTextField.MaximumDepth == 1 && ObservedTextField.OffMainThreadDraws == 0,
                "Paced scaling caused reentrant or off-main-thread drawing.", "contentview.paced-redraw");
            context.Assert(pixels > 5, "Paced scaling lost cold text.", "contentview.paced-text");
        }
        autoresizedChild.RemoveFromSuperview();
        context.Pass("Compositor-paced scaling preserves text without synchronous redraw");
    }

    async Task<(RuntimeBitmap bitmap, int inside, int outside, int changed)>
        CaptureChangedCompositorFrame(RuntimeTestContext context, NSWindow window, string name,
            RuntimeBitmap previous, NSView windowContent, NSView clip)
    {
        var current = previous;
        var inside = 0;
        var outside = 0;
        var changed = 0;
        for (var attempt = 0; attempt < 20 && changed <= 5; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(16);
            current = await CaptureAsync(context, window, name);
            inside = CountPixels(current, window, clip, IsMagenta);
            outside = CountPixels(current, window, windowContent, IsMagenta) - inside;
            changed = CountChangedMagentaPixels(previous, current);
        }
        return (current, inside, outside, changed);
    }

    static int CountChangedMagentaPixels(RuntimeBitmap first, RuntimeBitmap second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
            throw new InvalidOperationException("Compositor frame dimensions changed during zoom.");
        var changed = 0;
        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                var firstOffset = y * first.BytesPerRow + x * first.SamplesPerPixel;
                var secondOffset = y * second.BytesPerRow + x * second.SamplesPerPixel;
                if (IsMagenta(first.Pixels[firstOffset], first.Pixels[firstOffset + 1], first.Pixels[firstOffset + 2]) !=
                    IsMagenta(second.Pixels[secondOffset], second.Pixels[secondOffset + 1], second.Pixels[secondOffset + 2]))
                    changed++;
            }
        }
        return changed;
    }

    static bool FramesMatch(CGRect actual, CGRect expected) =>
        Math.Abs(actual.X - expected.X) < 0.0001 && Math.Abs(actual.Y - expected.Y) < 0.0001 &&
        Math.Abs(actual.Width - expected.Width) < 0.0001 && Math.Abs(actual.Height - expected.Height) < 0.0001;

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
    static bool IsLime(byte red, byte green, byte blue) => red < 10 && green > 240 && blue < 10;

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

sealed class ObservedContentViewHandler : ContentViewHandler
{
    public int ArrangeCalls { get; private set; }

    public override void PlatformArrange(Rect rect)
    {
        ArrangeCalls++;
        base.PlatformArrange(rect);
    }
}

sealed class ObservedToolbarDelegate(NSToolbarItem item) : NSObject, INSToolbarDelegate
{
    [Export("toolbar:itemForItemIdentifier:willBeInsertedIntoToolbar:")]
    public NSToolbarItem ToolbarItemForIdentifier(NSToolbar toolbar, string identifier, bool inserted) => item;

    [Export("toolbarAllowedItemIdentifiers:")]
    public string[] ToolbarAllowedItemIdentifiers(NSToolbar toolbar) => [item.Identifier];

    [Export("toolbarDefaultItemIdentifiers:")]
    public string[] ToolbarDefaultItemIdentifiers(NSToolbar toolbar) => [item.Identifier];
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
