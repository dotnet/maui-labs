using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.LayoutRegressionTests;

static class Program
{
    static void Main(string[] args)
    {
        using var timeout = new System.Threading.Timer(_ =>
        {
            Console.Error.WriteLine("FAIL AppKit dynamic layout regression timed out after 30 seconds");
            Environment.Exit(1);
        }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        NSApplication.Init();
        using var appDelegate = new RegressionDelegate();
        NSApplication.SharedApplication.Delegate = appDelegate;
        NSApplication.Main(args);
    }
}

public sealed class TestApplication : Application
{
}

[Register("LayoutRegressionDelegate")]
public sealed class RegressionDelegate : NSApplicationDelegate
{
    MauiApp? _app;
    NSWindow? _window;
    VerticalStackLayout _stack = null!;
    NSView _nativeStack = null!;

    public override void DidFinishLaunching(NSNotification notification)
    {
        try
        {
            _app = MauiApp.CreateBuilder().UseMauiAppMacOS<TestApplication>().Build();
            var context = new MacOSMauiContext(_app.Services);
            _stack = new VerticalStackLayout();
            _stack.Children.Add(new Label { Text = "Existing child" });
            _nativeStack = _stack.ToMacOSPlatform(context);
            _nativeStack.Appearance = NSAppearance.GetAppearance(NSAppearance.NameAqua);
            _window = new NSWindow(new CGRect(0, 0, 640, 480),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false);
            _window.ContentView = _nativeStack;
            _window.MakeKeyAndOrderFront(null);
            _nativeStack.LayoutSubtreeIfNeeded();

            // Mutate only after the handler is connected and the native window is shown.
            BeginInvokeOnMainThread(RunTests);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    void RunTests()
    {
        try
        {
            var existing = _stack.Children[0];
            AssertChildren(existing);

            var row = new HorizontalStackLayout
            {
                Children =
                {
                    new Label { Text = "Caption" },
                    new Label { Text = "Value" },
                },
            };
            _stack.Children.Add(row);
            _nativeStack.LayoutSubtreeIfNeeded();
            CaptureEvidence();
            AssertChildren(existing, row);
            AssertMeasured(row);
            foreach (var child in row.Children)
                AssertMeasured(child);
            Console.WriteLine("PASS Add after connection creates and lays out nested children");

            var inserted = new Label { Text = "Inserted first" };
            _stack.Children.Insert(0, inserted);
            AssertChildren(inserted, existing, row);
            Console.WriteLine("PASS Insert preserves native child order");

            var oldNative = Native(existing);
            var replacement = new Label { Text = "Replacement" };
            _stack.Children[1] = replacement;
            AssertChildren(inserted, replacement, row);
            Assert(oldNative.Superview == null, "Update left the replaced child attached");
            Console.WriteLine("PASS Update replaces and detaches the old child");

            var insertedNative = Native(inserted);
            _stack.Children.Remove(inserted);
            AssertChildren(replacement, row);
            Assert(insertedNative.Superview == null, "Remove left the child attached");
            Console.WriteLine("PASS Remove detaches the requested child");

            var replacementNative = Native(replacement);
            var rowNative = Native(row);
            _stack.Children.Clear();
            AssertChildren();
            Assert(replacementNative.Superview == null && rowNative.Superview == null,
                "Clear left children attached");
            Console.WriteLine("PASS Clear detaches all children");

            _stack.Children.Add(row);
            AssertChildren(row);
            _nativeStack.LayoutSubtreeIfNeeded();
            AssertMeasured(row);
            foreach (var child in row.Children)
                AssertMeasured(child);
            Console.WriteLine("PASS Re-add after Clear attaches once and lays out");

            _window!.Close();
            _app!.Dispose();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    void AssertChildren(params IView[] expected)
    {
        var actual = _nativeStack.Subviews;
        Assert(actual.Length == expected.Length,
            $"Expected {expected.Length} native children, got {actual.Length}");
        for (var i = 0; i < expected.Length; i++)
            Assert(actual[i].Handle == Native(expected[i]).Handle,
                $"Wrong native child at index {i}");
    }

    void CaptureEvidence()
    {
        var directory = Environment.GetEnvironmentVariable("APPKIT_LAYOUT_ARTIFACTS");
        if (string.IsNullOrEmpty(directory))
            return;

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "after-add.json"),
            System.Text.Json.JsonSerializer.Serialize(Inspect(_stack),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var bounds = _nativeStack.Bounds;
        var scale = _window!.BackingScaleFactor;
        using var bitmap = new NSBitmapImageRep(IntPtr.Zero,
            (nint)(bounds.Width * scale), (nint)(bounds.Height * scale),
            8, 4, true, false, NSColorSpace.DeviceRGB, 0, 0);
        bitmap.Size = bounds.Size;
        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            using var context = NSGraphicsContext.FromBitmap(bitmap)
                ?? throw new InvalidOperationException("Could not create screenshot context");
            NSGraphicsContext.CurrentContext = context;
            _nativeStack.CacheDisplay(bounds, bitmap);
            // CacheDisplay leaves the window backdrop transparent; fill behind the captured pixels.
            context.CGContext.SetBlendMode(CGBlendMode.DestinationOver);
            context.CGContext.SetFillColor(1, 1, 1, 1);
            context.CGContext.FillRect(new CGRect(0, 0, bitmap.PixelsWide, bitmap.PixelsHigh));
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }

        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("Could not encode screenshot");
        File.WriteAllBytes(Path.Combine(directory, "after-add.png"), png.ToArray());
    }

    static object Inspect(IView view) => new
    {
        Type = view.GetType().Name,
        Frame = view.Frame.ToString(),
        NativeFrame = (view.Handler?.PlatformView as NSView)?.Frame.ToString(),
        Children = (view as Microsoft.Maui.ILayout)?.Select(Inspect).ToArray(),
    };

    static void AssertMeasured(IView view)
    {
        var frame = Native(view).Frame;
        Assert(double.IsFinite(view.Frame.Width) && view.Frame.Width > 0 &&
            double.IsFinite(view.Frame.Height) && view.Frame.Height > 0 &&
            frame.Width > 0 && frame.Height > 0,
            $"{view.GetType().Name} has invalid managed/native bounds: {view.Frame} / {frame}");
    }

    static NSView Native(IView view)
        => view.Handler?.PlatformView as NSView
            ?? throw new InvalidOperationException($"{view.GetType().Name} has no AppKit view");

    static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    static void Fail(Exception ex)
    {
        Console.Error.WriteLine($"FAIL AppKit dynamic layout regression: {ex}");
        Environment.Exit(1);
    }
}
