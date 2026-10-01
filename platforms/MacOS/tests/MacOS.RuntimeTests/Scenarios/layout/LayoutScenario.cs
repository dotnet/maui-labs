using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;
using System.Runtime.CompilerServices;

namespace MacOS.RuntimeTests.Scenarios.Layout;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("layout", 6, context => new RegressionDelegate(context)));
}

public sealed class TestApplication : Application
{
}

[Register("LayoutRegressionDelegate")]
public sealed class RegressionDelegate(RuntimeTestContext context) : NSApplicationDelegate
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
            context.Fail(ex);
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
            context.Pass("Add after connection creates and lays out nested children");

            var inserted = new Label { Text = "Inserted first" };
            _stack.Children.Insert(0, inserted);
            AssertChildren(inserted, existing, row);
            context.Pass("Insert preserves native child order");

            var oldNative = Native(existing);
            var replacement = new Label { Text = "Replacement" };
            _stack.Children[1] = replacement;
            AssertChildren(inserted, replacement, row);
            Assert(oldNative.Superview == null, "Update left the replaced child attached");
            context.Pass("Update replaces and detaches the old child");

            var insertedNative = Native(inserted);
            _stack.Children.Remove(inserted);
            AssertChildren(replacement, row);
            Assert(insertedNative.Superview == null, "Remove left the child attached");
            context.Pass("Remove detaches the requested child");

            var replacementNative = Native(replacement);
            var rowNative = Native(row);
            _stack.Children.Clear();
            AssertChildren();
            Assert(replacementNative.Superview == null && rowNative.Superview == null,
                "Clear left children attached");
            context.Pass("Clear detaches all children");

            _stack.Children.Add(row);
            AssertChildren(row);
            _nativeStack.LayoutSubtreeIfNeeded();
            AssertMeasured(row);
            foreach (var child in row.Children)
                AssertMeasured(child);
            context.Pass("Re-add after Clear attaches once and lays out");

            _window!.Close();
            _app!.Dispose();
            context.Complete();
        }
        catch (Exception ex)
        {
            context.Fail(ex);
        }
    }

    void AssertChildren(params IView[] expected)
    {
        var actual = _nativeStack.Subviews;
        context.Assert(actual.Length == expected.Length,
            $"Expected {expected.Length} native children, got {actual.Length}", "layout.native-child-count");
        for (var i = 0; i < expected.Length; i++)
            Assert(actual[i].Handle == Native(expected[i]).Handle,
                $"Wrong native child at index {i}");
    }

    void CaptureEvidence()
    {
        context.WriteJson("after-add.json", Inspect(_stack));
        context.Capture(_nativeStack, "after-add.png");
    }

    static object Inspect(IView view) => new
    {
        Type = view.GetType().Name,
        Frame = view.Frame.ToString(),
        NativeFrame = (view.Handler?.PlatformView as NSView)?.Frame.ToString(),
        Children = (view as Microsoft.Maui.ILayout)?.Select(Inspect).ToArray(),
    };

    void AssertMeasured(IView view)
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

    void Assert(bool condition, string message) => context.Assert(condition, message);
}
