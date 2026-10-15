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

namespace MacOS.RuntimeTests.Scenarios.CollectionViewAsync;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("collection-view-async", 3,
            context => new CollectionViewAsyncScenario().CreateDelegate(context)));
}

sealed class CollectionViewAsyncScenario : MauiRuntimeScenario
{
    readonly ObservableCollection<string> _items = new();
    readonly List<Label> _labels = new();
    CollectionView _collection = null!;
    Label _control = null!;
    Label _empty = null!;

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
                    HeightRequest = 44,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                };
                grid.Add(caption, 0);
                grid.Add(value, 1);
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
            _empty.IsVisible = true;
            await RuntimeTestContext.FlushMainQueueAsync();
            await Task.Delay(50);
            context.Assert(NSThread.IsMain, "Async insertions resume on AppKit's UI thread.");
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
            context.Assert(native.Bounds.Height > 100, "The content-sized list reserves height for its rows.");
            context.Assert(clipHasHeight, "The native clip view follows the arranged list height.");
            context.Assert(rowsDraw, "Every realized row label has visible magenta glyphs in the compositor capture.");
            context.Pass($"Async insertion and redraw cycle {cycle + 1}");
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
