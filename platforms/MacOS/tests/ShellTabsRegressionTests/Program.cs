using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

static class Program
{
    internal static readonly string Output = Environment.GetEnvironmentVariable("SHELL_TABS_RESULTS")
        ?? Path.Combine(Environment.CurrentDirectory, "shell-tabs-results");

    static void Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        using var watchdog = new System.Threading.Timer(_ =>
        {
            File.WriteAllText(Path.Combine(Output, "failure.txt"), "Timed out waiting for AppKit regression run.");
            Environment.Exit(1);
        }, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new RegressionDelegate();
        NSApplication.Main(args);
    }
}

[Register("ShellTabsRegressionDelegate")]
sealed class RegressionDelegate : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
        .UseMauiAppMacOS<RegressionApp>()
        .AddMacOSEssentials()
        .Build();

    protected override void OnStarted() =>
        NSApplication.SharedApplication.BeginInvokeOnMainThread(Run);

    void Run()
    {
        try
        {
            var app = (RegressionApp)Application;
            var window = (NSWindow)app.Windows[0].Handler!.PlatformView!;
            var shell = app.TestShell;
            var root = window.ContentView!;
            window.SetContentSize(new CGSize(480, 640));
            root.LayoutSubtreeIfNeeded();
            window.Display();
            Capture(root, "initial");

            var tabs = Descendants(root).OfType<NSSegmentedControl>().SingleOrDefault();
            if (tabs == null)
            {
                File.WriteAllText(Path.Combine(Program.Output, "failure.txt"),
                    "REGRESSION: missing native tabs for six-section TabBar");
                Environment.Exit(42);
            }
            Require(tabs.SegmentCount == 6, "six native segments");
            Require(IsVisible(tabs) && tabs.VisibleRect().Width > 0 && tabs.VisibleRect().Height > 0,
                "tabs visible in a 480 DIP window");
            Require(app.CreatedPages == 1, "only initial page materialized");
            Require(shell.CurrentPage?.Title == "Page 0", "initial page displayed");

            tabs.SelectedSegment = 5;
            tabs.PerformClick(tabs);
            root.LayoutSubtreeIfNeeded();
            Require(shell.CurrentItem.CurrentItem == shell.CurrentItem.Items[5], "native click selects sixth section");
            Require(shell.CurrentPage?.Title == "Page 5", "native click displays selected page");
            Require(shell.CurrentPage?.Handler?.PlatformView is NSView pageView && pageView.Superview != null,
                "selected page attached to native tree");
            Require(app.CreatedPages == 2, "inactive page templates remain lazy");
            Capture(root, "selected-sixth");

            shell.CurrentItem.Items[1].IsVisible = false;
            shell.CurrentItem.Items[2].Title = "Renamed";
            root.LayoutSubtreeIfNeeded();
            Require(tabs.SegmentCount == 5 && tabs.SelectedSegment == 4, "hidden section keeps selected identity");
            Require(tabs.GetLabel(1) == "Renamed", "renamed section reflected");

            Shell.SetTabBarIsVisible(shell.CurrentPage, false);
            root.LayoutSubtreeIfNeeded();
            Require(!IsVisible(tabs), "page hides tabs");
            Shell.SetTabBarIsVisible(shell.CurrentPage, true);
            root.LayoutSubtreeIfNeeded();
            Require(IsVisible(tabs), "page restores tabs");

            var flyout = app.CreateItem(false, 3);
            shell.Items.Add(flyout);
            shell.CurrentItem = flyout;
            root.LayoutSubtreeIfNeeded();
            Require(tabs.SegmentCount == 3 && IsVisible(tabs), "multi-section FlyoutItem renders tabs");
            Capture(root, "flyout-sections");

            var replacement = app.CreateItem(true, 6);
            shell.Items.Add(replacement);
            shell.CurrentItem = replacement;
            shell.Items.Remove(flyout);
            window.SetContentSize(new CGSize(320, 640));
            root.LayoutSubtreeIfNeeded();
            Require(tabs.SegmentCount == 6 && IsVisible(tabs), "replacement TabBar survives narrow resize");
            Require(tabs.EnclosingScrollView != null, "overflow tabs remain horizontally scrollable");
            tabs.ScrollRectToVisible(tabs.Bounds);
            Capture(root, "narrow-replacement");

            File.WriteAllText(Path.Combine(Program.Output, "passed.txt"),
                $"PASS: native AppKit tabs, selection, lazy pages, metadata, visibility, replacement and resize. OS={Environment.OSVersion}");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Program.Output, "failure.txt"), ex.ToString());
            Console.Error.WriteLine(ex);
            Environment.Exit(1);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        File.AppendAllText(Path.Combine(Program.Output, "assertions.txt"), $"PASS: {message}\n");
    }

    static bool IsVisible(NSView view)
    {
        for (NSView? current = view; current != null; current = current.Superview)
            if (current.Hidden)
                return false;
        return true;
    }

    static IEnumerable<NSView> Descendants(NSView view)
    {
        yield return view;
        foreach (var child in view.Subviews)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    static void Capture(NSView view, string name)
    {
        using var bitmap = new NSBitmapImageRep(IntPtr.Zero, (nint)view.Bounds.Width,
            (nint)view.Bounds.Height, 8, 4, true, false, NSColorSpace.DeviceRGB, 0, 0);
        bitmap.Size = view.Bounds.Size;
        view.CacheDisplay(view.Bounds, bitmap);
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(Program.Output, $"{name}.png"), png.ToArray());
    }
}

public sealed class RegressionApp : Application
{
    public Shell TestShell { get; } = new() { FlyoutBehavior = FlyoutBehavior.Disabled };
    public int CreatedPages { get; private set; }

    public RegressionApp() => TestShell.Items.Add(CreateItem(true, 6));

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(TestShell) { Width = 480, Height = 640 };

    internal ShellItem CreateItem(bool tabBar, int count)
    {
        ShellItem item = tabBar ? new TabBar() : new FlyoutItem();
        for (var i = 0; i < count; i++)
        {
            var index = i;
            item.Items.Add(new Tab
            {
                Title = $"Tab {index}",
                Items =
                {
                    new ShellContent
                    {
                        ContentTemplate = new DataTemplate(() =>
                        {
                            CreatedPages++;
                            return new ContentPage
                            {
                                Title = $"Page {index}",
                                Content = new Label { Text = $"Selected page {index}", FontSize = 32 },
                            };
                        }),
                    },
                },
            });
        }
        return item;
    }
}
