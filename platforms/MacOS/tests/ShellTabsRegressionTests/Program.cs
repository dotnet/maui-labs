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
        NSApplication.SharedApplication.Appearance = NSAppearance.GetAppearance(NSAppearance.NameAqua);
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
        .ConfigureMauiHandlers(handlers => handlers.AddHandler<ContentPage, CountingPageHandler>())
        .Build();

    protected override void OnStarted() =>
        NSApplication.SharedApplication.BeginInvokeOnMainThread(Run);

    async void Run()
    {
        try
        {
            var app = (RegressionApp)Application;
            var window = (NSWindow)app.Windows[0].Handler!.PlatformView!;
            var shell = app.TestShell;
            var root = window.ContentView!;
            window.SetContentSize(new CGSize(480, 640));
            await FlushMainQueue();
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
            await FlushMainQueue();
            root.LayoutSubtreeIfNeeded();
            Require(shell.CurrentItem.CurrentItem == shell.CurrentItem.Items[5], "native click selects sixth section");
            Require(shell.CurrentPage?.Title == "Page 5", "native click displays selected page");
            Require(shell.CurrentPage?.Handler?.PlatformView is NSView pageView && pageView.Superview != null,
                "selected page attached to native tree");
            Require(Descendants(root).OfType<NSTextField>().Any(label =>
                label.StringValue == "Selected page 5" && IsVisible(label) &&
                label.Bounds.Width > 0 && label.Bounds.Height > 0), "selected page label rendered");
            Require(app.CreatedPages == 2, "inactive page templates remain lazy");
            Capture(root, "selected-sixth");
            var selectedView = (CountingPageView)shell.CurrentPage!.Handler!.PlatformView!;
            File.WriteAllText(Path.Combine(Program.Output, "click-refresh-count.txt"),
                $"Selected page 5 native attachments: {selectedView.Attachments}");
            Require(selectedView.Attachments == 1,
                $"native click attaches the selected page once (actual {selectedView.Attachments})");

            shell.FlyoutBehavior = FlyoutBehavior.Locked;
            window.SetContentSize(new CGSize(1000, 640));
            await FlushMainQueue();
            root.LayoutSubtreeIfNeeded();
            window.Display();
            var sidebar = Descendants(root).OfType<NSOutlineView>().Single();
            Require(IsVisible(sidebar) && sidebar.VisibleRect().Width > 0,
                "wide window displays native sidebar");
            var sidebarCell = Descendants(sidebar).OfType<NSTableCellView>()
                .Single(cell => cell.TextField?.StringValue == "Tab 5");
            Require(sidebarCell.TextField is { } title && IsVisible(title) && title.Bounds.Width > 0,
                "wide sidebar retains selected tab title");
            Require(sidebarCell.ImageView is { Image: not null } icon && IsVisible(icon) &&
                icon.Bounds.Width > 0 && icon.Bounds.Height > 0, "wide sidebar retains system icon");
            Require(tabs.GetLabel(5) == "Tab 5" && shell.CurrentPage.Title == "Page 5",
                "wide resize preserves tab title and page");
            Capture(root, "wide-sidebar");

            shell.FlyoutBehavior = FlyoutBehavior.Disabled;
            window.SetContentSize(new CGSize(480, 640));
            await FlushMainQueue();
            root.LayoutSubtreeIfNeeded();
            Require((!IsVisible(sidebar) || sidebar.VisibleRect().Width <= 0) && IsVisible(tabs),
                "narrow presentation restores tabs without sidebar");
            tabs.SelectedSegment = 0;
            tabs.PerformClick(tabs);
            await FlushMainQueue();
            Require(shell.CurrentPage?.Title == "Page 0", "native click returns to cached first page");
            var previousAttachments = selectedView.Attachments;
            tabs.SelectedSegment = 5;
            tabs.PerformClick(tabs);
            await FlushMainQueue();
            Require(shell.CurrentPage?.Title == "Page 5" && selectedView.Superview != null,
                "native click restores cached selected page");
            Require(selectedView.Attachments == previousAttachments + 1,
                "native click attaches cached page once");
            Require(app.CreatedPages == 2, "native return visits reuse cached pages");

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

            flyout.CurrentItem = flyout.Items[^1];
            RunSelectionSteps(app, root, shell, tabs, replacement);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Program.Output, "failure.txt"), ex.ToString());
            Console.Error.WriteLine(ex);
            Environment.Exit(1);
        }
    }

    static async Task FlushMainQueue()
    {
        for (var i = 0; i < 2; i++)
        {
            var completion = new TaskCompletionSource();
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() => completion.SetResult());
            await completion.Task;
        }
    }

    static void RunSelectionSteps(RegressionApp app, NSView root, Shell shell, NSSegmentedControl tabs, ShellItem item)
    {
        NSView? pageBeforeDisconnect = null;
        string? firstLabel = null;
        var steps = new Queue<Action>(new Action[]
        {
            () =>
            {
                AssertSelectedPage("detached-item-selection");
                item.CurrentItem = item.Items[4];
            },
            () =>
            {
                AssertSelectedPage("programmatic-selection");
                item.CurrentItem.IsVisible = false;
            },
            () =>
            {
                AssertSelectedPage("hide-active-section");
                item.Items.Remove(item.CurrentItem);
            },
            () =>
            {
                AssertSelectedPage("remove-active-section");
                var handler = shell.Handler!;
                var previousItem = item;
                item.CurrentItem = item.Items[^1];
                shell = new Shell { FlyoutBehavior = FlyoutBehavior.Disabled };
                item = app.CreateItem(true, 2);
                shell.Items.Add(item);
                handler.SetVirtualView(shell);
                previousItem.Items[0].Title = "Changed after rebind";
                previousItem.CurrentItem = previousItem.Items[0];
            },
            () =>
            {
                AssertSelectedPage("handler-rebind");
                Require(tabs.SegmentCount == 2 && tabs.GetLabel(0) == "Tab 0",
                    "rebind: new shell tabs replace old subscriptions");
                pageBeforeDisconnect = shell.CurrentPage.Handler!.PlatformView as NSView;
                item.CurrentItem = item.Items[^1];
                shell.Handler!.DisconnectHandler();
                firstLabel = tabs.GetLabel(0);
                item.Items[0].Title = "Changed after disconnect";
            },
            () =>
            {
                Require(pageBeforeDisconnect != null && Descendants(root).Contains(pageBeforeDisconnect),
                    "disconnect: queued selection did not replace native page");
                Require(tabs.GetLabel(0) == firstLabel, "disconnect: tab metadata observer detached");
                File.WriteAllText(Path.Combine(Program.Output, "passed.txt"),
                    $"PASS: native tabs and page coherence for click, programmatic, hidden and removed selections. OS={Environment.OSVersion}");
                Environment.Exit(0);
            },
        });
        NSApplication.SharedApplication.BeginInvokeOnMainThread(RunNext);

        void RunNext()
        {
            try
            {
                steps.Dequeue()();
                // Child-handler selection refresh is deferred until MAUI finishes updating its model.
                if (steps.Count > 0)
                    NSApplication.SharedApplication.BeginInvokeOnMainThread(RunNext);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Program.Output, "failure.txt"), ex.ToString());
                Console.Error.WriteLine(ex);
                Environment.Exit(1);
            }
        }

        void AssertSelectedPage(string scenario)
        {
            root.LayoutSubtreeIfNeeded();
            Capture(root, scenario);
            var selected = item.CurrentItem;
            var visibleSections = item.Items.Where(section => section.IsVisible).ToList();
            Require(tabs.SelectedSegment == visibleSections.IndexOf(selected), $"{scenario}: native selected identity");
            var expectedTitle = selected.Title.Replace("Tab ", "Page ");
            var expectedPage = shell.CurrentPage;
            Require(expectedPage != null && expectedPage.Title == expectedTitle,
                $"{scenario}: selected page materialized");
            Require(expectedPage?.Handler?.PlatformView is NSView nativePage &&
                Descendants(root).Contains(nativePage), $"{scenario}: selected page in native tree");
            Require(Descendants(root).OfType<NSTextField>().Any(label =>
                label.StringValue == expectedPage?.Title?.Replace("Page ", "Selected page ") &&
                IsVisible(label) && label.Bounds.Width > 0 && label.Bounds.Height > 0),
                $"{scenario}: selected page label rendered");
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
        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            using var context = NSGraphicsContext.FromBitmap(bitmap)
                ?? throw new InvalidOperationException("Bitmap graphics context unavailable.");
            NSGraphicsContext.CurrentContext = context;
            // View caching does not include the NSWindow's opaque background.
            context.CGContext.SetFillColor(NSColor.White.CGColor);
            context.CGContext.FillRect(view.Bounds);
            view.CacheDisplay(view.Bounds, bitmap);
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(Program.Output, $"{name}.png"), png.ToArray());
    }
}

public sealed class RegressionApp : Application
{
    public Shell TestShell { get; } = new() { FlyoutBehavior = FlyoutBehavior.Disabled };
    public int CreatedPages { get; private set; }

    public RegressionApp()
    {
        MacOSShell.SetUseNativeSidebar(TestShell, true);
        TestShell.Items.Add(CreateItem(true, 6));
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(TestShell) { Width = 480, Height = 640 };

    internal ShellItem CreateItem(bool tabBar, int count)
    {
        ShellItem item = tabBar ? new TabBar() : new FlyoutItem();
        MacOSShell.SetSystemImage(item, "star");
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

        sealed class CountingPageHandler : Handlers.ContentPageHandler
        {
            protected override MacOSContainerView CreatePlatformView() => new CountingPageView();
        }

        sealed class CountingPageView : MacOSContainerView
        {
            public int Attachments { get; private set; }

            public override void ViewDidMoveToSuperview()
            {
                base.ViewDidMoveToSuperview();
                if (Superview != null)
                    Attachments++;
            }
        }
        return item;
    }
}
