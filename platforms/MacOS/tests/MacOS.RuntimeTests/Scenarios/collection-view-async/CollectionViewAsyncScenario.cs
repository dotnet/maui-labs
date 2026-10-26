using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace MacOS.RuntimeTests.Scenarios.CollectionViewAsync;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("collection-view-async", 5,
            context => new CollectionViewAsyncScenario().CreateDelegate(context)));
}

sealed class CollectionViewAsyncScenario : MauiRuntimeScenario
{
    readonly ObservableCollection<string> _items = new();
    readonly List<Label> _labels = new();
    readonly List<Grid> _rows = new();
    CollectionView _collection = null!;
    Label _control = null!;
    Label _empty = null!;
    double _rowHeight = 44;

    public override void Configure(MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<CollectionView, ObservedCollectionViewHandler>());

    public override Window CreateWindow(IActivationState? activationState)
    {
        _control = new Label
        {
            Text = "Independent visible control",
            TextColor = Colors.Cyan,
            BackgroundColor = Colors.DarkRed,
            HeightRequest = 44,
        };
        _collection = new CollectionView
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var caption = new Label { TextColor = Colors.Magenta };
                caption.SetBinding(Label.TextProperty, ".");
                var value = new Label { Text = "Async detail", TextColor = Colors.Magenta };
                _labels.Add(caption);
                _labels.Add(value);
                var grid = new Grid
                {
                    HeightRequest = _rowHeight,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                };
                grid.Add(caption, 0);
                grid.Add(value, 1);
                _rows.Add(grid);
                return grid;
            }),
        };
        _empty = new Label { Text = "No rows yet" };
        var page = new ContentPage
        {
            Title = "Async rows",
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 24,
                    Children = { _control, _empty, _collection, new Button { Text = "Below rows" } },
                },
            },
        };
        var shell = new Shell();
        shell.Items.Add(new ShellContent { Content = page });
        return new Window(shell) { Width = 640, Height = 640, Title = "Async CollectionView regression" };
    }

    public override async Task RunAsync(RuntimeTestContext context, Window window)
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        var native = (NSScrollView)_collection.Handler!.PlatformView!;
        var nativeWindow = native.Window ?? throw new InvalidOperationException("CollectionView is not in a window.");
        await CaptureAsync(context, nativeWindow, "empty.png");
        context.Assert(_items.Count == 0,
            "The CollectionView starts empty after the Shell page appears.");

        for (var cycle = 0; cycle < 3; cycle++)
        {
            _items.Clear();
            _labels.Clear();
            _rows.Clear();
            _empty.IsVisible = true;
            await RuntimeTestContext.FlushMainQueueAsync();
            await Task.Delay(50);
            context.Assert(NSThread.IsMain, "Async insertions resume on AppKit's UI thread.");
            _rowHeight = 36 + cycle * 10;
            for (var i = 0; i < cycle + 3; i++)
                _items.Add($"Row {cycle + 1}.{i + 1}");
            _empty.IsVisible = false;
            var image = await CaptureAsync(context, nativeWindow, $"rows-{cycle}.png");
            var document = native.DocumentView ?? throw new InvalidOperationException("Missing document view.");
            var rows = document.Subviews.Single().Subviews;
            var labels = _labels.Where(label =>
                label.Handler?.PlatformView is NSView view && view.Window == nativeWindow).ToArray();
            var glyphs = labels.Select(label => CountPixels(image, nativeWindow, Native(label), cyan: false)).ToArray();
            context.WriteJson($"rows-{cycle}.json", new
            {
                scrollBounds = native.Bounds.ToString(),
                clipBounds = native.ContentView.Bounds.ToString(),
                documentBounds = document.Bounds.ToString(),
                rows = rows.Select(row => new { frame = row.Frame.ToString(), visible = row.VisibleRect().ToString() }),
                labels = labels.Select(label => new
                {
                    label.Text,
                    bounds = Native(label).Bounds.ToString(),
                    visible = Native(label).VisibleRect().ToString(),
                }),
                glyphs,
            });
            context.Assert(rows.Length == _items.Count && labels.Length == _items.Count * 2,
                "Every async item has a realized Grid and two attached labels.");
            context.Assert(labels.All(label => Native(label).Bounds.Width > 0 && Native(label).Bounds.Height > 0),
                "Realized labels have positive native bounds.");
            var clipHasHeight = native.ContentView.Bounds.Height >= native.Bounds.Height - 1;
            var rowsDraw = glyphs.All(count => count > 5);
            if (cycle == 0 && native.Bounds.Height == 0 && document.Bounds.Height > 100 && glyphs.All(count => count == 0))
            {
                context.Assert(native.ContentView.Bounds.Height == 0 && rows.All(row => row.VisibleRect().Height == 0),
                    "The realized document is clipped by a zero-height CollectionView viewport.");
                context.BaselineFailure("collection-view-async.zero-height",
                    "BASELINE_685_SIZING: async rows are realized in a positive-height document inside a zero-height CollectionView.");
                return;
            }
            context.Assert(Math.Abs(native.Bounds.Height - _items.Count * _rowHeight) < 1,
                "The content-sized list follows actual row measurements, not the estimated height.");
            context.Assert(clipHasHeight, "The native clip view follows the arranged list height.");
            context.Assert(rowsDraw, "Every realized row label has visible magenta glyphs in the compositor capture.");
            context.Pass($"Async insertion and redraw cycle {cycle + 1}");
        }

        var resizedRow = _rows[0];
        var nativeResizedRow = (NSView)resizedRow.Handler!.PlatformView!;
        var previousListHeight = native.Bounds.Height;
        var previousRowHeight = resizedRow.HeightRequest;
        var resizedRowHeight = previousRowHeight + 34;
        resizedRow.HeightRequest = resizedRowHeight;
        var resizedImage = await CaptureAsync(context, nativeWindow, "resized.png");
        var expectedListHeight = previousListHeight + resizedRowHeight - previousRowHeight;
        context.WriteJson("resized.json", new
        {
            previousListHeight,
            previousRowHeight,
            resizedRowHeight,
            listHeight = native.Bounds.Height,
            rowBounds = nativeResizedRow.Bounds.ToString(),
            rowFrame = nativeResizedRow.Frame.ToString(),
        });
        context.Assert(Math.Abs(nativeResizedRow.Bounds.Height - resizedRowHeight) < 1,
            "A measured row updates its native height after its requested size changes.");
        context.Assert(Math.Abs(native.Bounds.Height - expectedListHeight) < 1,
            "The content-sized document extent includes a later measured-row size change.");
        context.Assert(CountPixels(resizedImage, nativeWindow, Native(_labels[0]), cyan: false) > 5,
            "The resized row remains visibly rendered after its measurement changes.");

        var settled = false;
        var previous = (ObservedCollectionViewHandler.Measures, ObservedCollectionViewHandler.Arranges);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(100);
            await RuntimeTestContext.FlushMainQueueAsync();
            var current = (ObservedCollectionViewHandler.Measures, ObservedCollectionViewHandler.Arranges);
            if (current == previous)
            {
                settled = true;
                break;
            }
            previous = current;
        }
        context.WriteJson("layout-convergence.json", new
        {
            settled,
            ObservedCollectionViewHandler.Measures,
            ObservedCollectionViewHandler.Arranges,
        });
        context.Assert(settled, "Content-measurement correction settles without perpetual measure/arrange invalidation.");

        _collection.HeightRequest = 112;
        _items.Clear();
        _labels.Clear();
        _rows.Clear();
        for (var i = 0; i < 20; i++)
            _items.Add($"Constrained row {i + 1}");
        await CaptureAsync(context, nativeWindow, "constrained.png");
        var constrainedDocument = native.DocumentView!;
        var realized = constrainedDocument.Subviews.Single().Subviews.Length;
        context.Assert(Math.Abs(native.Bounds.Height - 112) < 1,
            "An explicit viewport height is preserved instead of expanding to the document extent.");
        context.Assert(constrainedDocument.Bounds.Height > native.Bounds.Height && realized < _items.Count,
            "Constrained lists retain a scrollable document and virtualize offscreen rows.");
        _collection.ScrollTo(19, position: ScrollToPosition.End, animate: false);
        var scrolledImage = await CaptureAsync(context, nativeWindow, "constrained-scrolled.png");
        var visibleLabels = _labels.Where(label => label.Handler?.PlatformView is NSView view &&
            view.Window == nativeWindow && view.VisibleRect().Height > 0).ToArray();
        var visibleGlyphs = visibleLabels.Select(label => CountPixels(scrolledImage, nativeWindow, Native(label), cyan: false)).ToArray();
        context.WriteJson("constrained.json", new
        {
            bounds = native.Bounds.ToString(),
            clip = native.ContentView.Bounds.ToString(),
            document = constrainedDocument.Bounds.ToString(),
            realized,
            visibleGlyphs,
        });
        context.Assert(native.ContentView.Bounds.Y > 0, "Constrained lists still scroll to later items.");
        context.Assert(visibleGlyphs.Count(count => count > 5) >= 2,
            "Scrolled constrained rows have visible compositor glyphs.");
        context.Pass("Constrained viewport retains virtualization and internal scrolling");

        _collection.HeightRequest = -1;
        _collection.MinimumHeightRequest = 140;
        _collection.MaximumHeightRequest = 180;
        _items.Clear();
        _items.Add("Minimum-sized row");
        await CaptureAsync(context, nativeWindow, "minimum.png");
        context.Assert(Math.Abs(native.Bounds.Height - 140) < 1,
            "An unconstrained short document honors MinimumHeightRequest.");
        for (var i = 0; i < 5; i++)
            _items.Add($"Maximum-sized row {i + 1}");
        await CaptureAsync(context, nativeWindow, "maximum.png");
        context.Assert(Math.Abs(native.Bounds.Height - 180) < 1,
            "An unconstrained long document honors MaximumHeightRequest.");
        _collection.HeightRequest = 156;
        await CaptureAsync(context, nativeWindow, "explicit.png");
        context.Assert(Math.Abs(native.Bounds.Height - 156) < 1,
            "An explicit height inside the min/max limits is preserved.");
        context.WriteJson("size-limits.json", new
        {
            minimum = _collection.MinimumHeightRequest,
            maximum = _collection.MaximumHeightRequest,
            explicitHeight = _collection.HeightRequest,
            finalBounds = native.Bounds.ToString(),
        });
        context.Pass("Content sizing honors minimum, maximum and explicit height requests");
    }

    public sealed class ObservedCollectionViewHandler : CollectionViewHandler
    {
        public static int Measures { get; private set; }
        public static int Arranges { get; private set; }

        public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
        {
            Measures++;
            return base.GetDesiredSize(widthConstraint, heightConstraint);
        }

        public override void PlatformArrange(Rect rect)
        {
            Arranges++;
            base.PlatformArrange(rect);
        }
    }

    async Task<RuntimeBitmap> CaptureAsync(RuntimeTestContext context, NSWindow window, string name)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            await RuntimeTestContext.FlushMainQueueAsync();
            window.ContentView!.LayoutSubtreeIfNeeded();
            CATransaction.Flush();
            await Task.Delay(50);
            if (!NSThread.IsMain)
                throw new InvalidOperationException("Capture left AppKit's main thread.");
            var image = context.CaptureWindowBitmap(window, name);
            var fullSize = (image.Width == (int)window.Frame.Width && image.Height == (int)window.Frame.Height)
                || (image.Width == (int)(window.Frame.Width * window.BackingScaleFactor)
                    && image.Height == (int)(window.Frame.Height * window.BackingScaleFactor));
            if (fullSize && CountPixels(image, window, Native(_control), cyan: true) > 5)
                return image;
            if (timer.Elapsed.TotalSeconds > 5)
                throw new InvalidOperationException("Compositor capture did not contain the independent visible control.");
        }
    }

    static NSView Native(Label label) => (NSView)label.Handler!.PlatformView!;

    static int CountPixels(RuntimeBitmap image, NSWindow window, NSView view, bool cyan)
    {
        var bounds = view.ConvertRectToView(view.Bounds, null);
        var sx = image.Width / window.Frame.Width;
        var sy = image.Height / window.Frame.Height;
        var left = Math.Clamp((int)(bounds.X * sx), 0, image.Width);
        var right = Math.Clamp((int)(bounds.Right * sx), 0, image.Width);
        var top = Math.Clamp((int)((window.Frame.Height - bounds.Bottom) * sy), 0, image.Height);
        var bottom = Math.Clamp((int)((window.Frame.Height - bounds.Y) * sy), 0, image.Height);
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = y * image.BytesPerRow + x * image.SamplesPerPixel;
                var r = image.Pixels[offset];
                var g = image.Pixels[offset + 1];
                var b = image.Pixels[offset + 2];
                if (b > 150 && (cyan ? r < 140 && g > 150 : r > 150 && g < 140))
                    count++;
            }
        }
        return count;
    }
}
