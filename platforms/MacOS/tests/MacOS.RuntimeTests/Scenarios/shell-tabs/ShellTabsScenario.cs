using AppKit;
using CoreGraphics;
using System.Runtime.CompilerServices;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;
using Handlers = Microsoft.Maui.Platforms.MacOS.Handlers;

namespace MacOS.RuntimeTests.Scenarios.ShellTabs;

static class ShellTabsRegistration
{
    [ModuleInitializer]
    public static void Register() => ScenarioRegistry.Register(
        new("shell-tabs", ExpectedCases: 1,
            CreateDelegate: context => new ShellTabsScenario().CreateDelegate(context),
            ExpectedAssertions: 63));
}

sealed class ShellTabsScenario : MauiRuntimeScenario
{
    RuntimeTestContext _context = null!;
    Shell TestShell { get; } = new() { FlyoutBehavior = FlyoutBehavior.Disabled };
    int CreatedPages { get; set; }

    public override void Configure(MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<CountingPage, CountingPageHandler>());

    public override async Task RunAsync(RuntimeTestContext context, Window mauiWindow)
    {
        _context = context;
        try
        {
            var app = this;
            var window = (NSWindow)mauiWindow.Handler!.PlatformView!;
            var shell = app.TestShell;
            var root = window.ContentView!;
            window.SetContentSize(new CGSize(480, 640));
            await FlushMainQueue();
            root.LayoutSubtreeIfNeeded();
            window.Display();
            Capture(root, "initial");

            var tabs = Descendants(root).OfType<NSSegmentedControl>().SingleOrDefault()
                ?? throw new InvalidOperationException("REGRESSION: missing native tabs for six-section TabBar");
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
            var selectedPage = (CountingPage)shell.CurrentPage!;
            File.WriteAllText(context.EvidencePath("click-refresh-count.txt"),
                $"Selected page 5 native attachments: {selectedPage.NativeAttachments}");
            File.WriteAllText(context.EvidencePath("click-refresh-traces.txt"),
                string.Join("\n\n", selectedPage.NativeAttachmentTraces));
            context.WriteJson("click-refresh.json", new
            {
                page = selectedPage.Title,
                attachments = selectedPage.NativeAttachments,
                nativeViewCreations = selectedPage.NativeViewCreations,
            });
            context.Assert(selectedPage.NativeAttachments == 1,
                $"native click attaches the selected page once (actual {selectedPage.NativeAttachments})",
                "shell-tabs.single-click-attachment");

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
            var previousAttachments = selectedPage.NativeAttachments;
            tabs.SelectedSegment = 5;
            tabs.PerformClick(tabs);
            await FlushMainQueue();
            Require(ReferenceEquals(shell.CurrentPage, selectedPage) &&
                selectedPage.Handler?.PlatformView is NSView { Superview: not null },
                "native click restores cached selected page");
            Require(selectedPage.NativeAttachments == previousAttachments + 1,
                "native click attaches cached page once");
            Require(selectedPage.NativeViewCreations == 1, "cached page reuses its native handler and view");
            Require(app.CreatedPages == 2, "native return visits reuse cached pages");
            await VerifyMountRefresh(window, root, shell, selectedPage);

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
            await RunSelectionSteps(app, root, shell, tabs, replacement);
            context.Pass("shell-tabs-navigation-and-mount-lifecycle");
        }
        catch (Exception ex)
        {
            File.WriteAllText(context.EvidencePath("failure.txt"), ex.ToString());
            throw;
        }
    }

    static Task FlushMainQueue() => RuntimeTestContext.FlushMainQueueAsync();

    async Task VerifyMountRefresh(NSWindow window, NSView root, Shell shell, CountingPage page)
    {
        var shellHandler = shell.Handler!;
        var context = shellHandler.MauiContext!;
        var handler = page.Handler!;
        var view = (NSView)handler.PlatformView!;
        var host = view.Superview!;
        var attachments = page.NativeAttachments;
        var creations = page.NativeViewCreations;
        var title = page.Title;
        var action = new ToolbarItem { Text = "Refresh action" };
        page.ToolbarItems.Add(action);
        page.Title = "Refreshed page";
        window.Title = "Stale chrome";
        view.Frame = new CGRect(0, 0, 1, 1);
        shellHandler.UpdateValue(nameof(Shell.CurrentItem));
        await FlushMainQueue();
        Require(ReferenceEquals(page.Handler, handler) && page.NativeViewCreations == creations &&
            page.NativeAttachments == attachments, "same-page refresh reuses handler without remounting");
        Require(view.Frame.Width == host.Bounds.Width && view.Frame.Height == host.Bounds.Height,
            "same-page refresh restores native layout");
        Require(window.Title == "Refreshed page" &&
            window.Toolbar?.Items.Any(item => item.Label == "Refresh action") == true,
            "same-page refresh updates native title and toolbar");
        page.ToolbarItems.Remove(action);
        page.Title = title;
        shellHandler.UpdateValue(nameof(Shell.CurrentItem));
        await FlushMainQueue();

        view.RemoveFromSuperview();
        shellHandler.UpdateValue(nameof(Shell.CurrentItem));
        await FlushMainQueue();
        Require(ReferenceEquals(page.Handler, handler) && ReferenceEquals(view.Superview, host) &&
            page.NativeAttachments == attachments + 1, "detached current page remounts using its existing handler");

        var replacement = new CountingPageHandler();
        replacement.SetMauiContext(context);
        replacement.SetVirtualView(page);
        shellHandler.UpdateValue(nameof(Shell.CurrentItem));
        await FlushMainQueue();
        Require(ReferenceEquals(page.Handler, replacement) &&
            replacement.PlatformView.Superview == host && view.Superview == null,
            "same-context replacement handler supplies the mounted view");

        var foreign = new CountingPageHandler();
        foreign.SetMauiContext(new MauiContext(context.Services));
        foreign.SetVirtualView(page);
        shellHandler.UpdateValue(nameof(Shell.CurrentItem));
        await FlushMainQueue();
        Require(!ReferenceEquals(page.Handler, foreign) && ReferenceEquals(page.Handler?.MauiContext, context) &&
            page.Handler?.PlatformView is NSView { Superview: not null },
            "changed-context handler is converted for the current context");

        var disposedView = (NSView)page.Handler!.PlatformView!;
        disposedView.RemoveFromSuperview();
        disposedView.Dispose();
        var handleAfterDispose = disposedView.Handle;
        await FlushMainQueue();
        File.WriteAllText(_context.EvidencePath("disposed-view-state.txt"),
            $"immediate={handleAfterDispose}; after UI queue={disposedView.Handle}");
        Require(disposedView.Handle == IntPtr.Zero, "disposed native handle has been released");
        shellHandler.UpdateValue(nameof(Shell.CurrentItem));
        await FlushMainQueue();
        var resolvedView = page.Handler?.PlatformView as NSView;
        File.AppendAllText(_context.EvidencePath("disposed-view-state.txt"),
            $"; resolved={resolvedView?.Handle}; same view={ReferenceEquals(resolvedView, disposedView)}" +
            $"; context matches={ReferenceEquals(page.Handler?.MauiContext, context)}" +
            $"; parent={(resolvedView != null && resolvedView.Handle != IntPtr.Zero ? resolvedView.Superview?.Handle.ToString() : "none")}" +
            $"; expected parent={host.Handle}");
        Require(page.Handler?.PlatformView is NSView restoredView && restoredView.Handle != IntPtr.Zero &&
            !ReferenceEquals(restoredView, disposedView) && ReferenceEquals(restoredView.Superview, host),
            "disposed native view is replaced and mounted");

        var empty = new Shell { FlyoutBehavior = FlyoutBehavior.Disabled };
        shellHandler.SetVirtualView(empty);
        await FlushMainQueue();
        Require(host.Subviews.Length == 0, "empty shell clears the previous native page");
        shellHandler.SetVirtualView(shell);
        await FlushMainQueue();
        Require(ReferenceEquals(shell.CurrentPage, page) &&
            page.Handler?.PlatformView is NSView { Superview: not null }, "restored shell remounts its page");

        var retainedItem = shell.CurrentItem;
        var retainedSection = retainedItem.CurrentItem;
        var destinationPage = new CountingPage
        {
            Title = "New destination",
            Content = new Label { Text = "New destination" },
        };
        var destination = new TabBar
        {
            Items = { new Tab { Items = { new ShellContent { Content = destinationPage } } } },
        };
        CountingPage? abandonedPage = null;
        var redirect = new Tab
        {
            Title = "Redirect",
            Items =
            {
                new ShellContent
                {
                    ContentTemplate = new DataTemplate(() =>
                    {
                        // Switch a different BindableObject: a reentrant write to the same
                        // CurrentItem setter can be deferred until its outer setter completes.
                        shell.CurrentItem = destination;
                        Require(ReferenceEquals(shell.CurrentItem, destination) &&
                            destinationPage.Handler?.PlatformView is NSView { Superview: not null },
                            "newer destination is rendered during lazy creation");
                        return abandonedPage = new CountingPage
                        {
                            Title = "Stale destination",
                            Content = new Label { Text = "Stale destination" },
                        };
                    }),
                },
            },
        };
        shell.Items.Add(destination);
        retainedItem.Items.Add(redirect);
        retainedItem.CurrentItem = redirect;
        await FlushMainQueue();
        File.WriteAllText(_context.EvidencePath("reentrant-destination-state.txt"),
            $"page={shell.CurrentPage?.Title}; abandoned attachments={abandonedPage?.NativeAttachments}");
        Require(ReferenceEquals(shell.CurrentItem, destination) && ReferenceEquals(shell.CurrentPage, destinationPage),
            "navigation during lazy creation retains the newer destination");
        Require(abandonedPage is { NativeAttachments: 0 } && destinationPage.Handler?.PlatformView is NSView mounted &&
            Descendants(root).Contains(mounted), "older outer render never mounts the abandoned destination");
        Capture(root, "reentrant-different-destination");
        retainedItem.CurrentItem = retainedSection;
        retainedItem.Items.Remove(redirect);
        shell.CurrentItem = retainedItem;
        shell.Items.Remove(destination);
        await FlushMainQueue();
    }

    Task RunSelectionSteps(ShellTabsScenario app, NSView root, Shell shell, NSSegmentedControl tabs, ShellItem item)
    {
        var completion = new TaskCompletionSource();
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
                File.WriteAllText(_context.EvidencePath("passed.txt"),
                    $"PASS: native tabs and page coherence for click, programmatic, hidden and removed selections. OS={Environment.OSVersion}");
            },
        });
        NSApplication.SharedApplication.BeginInvokeOnMainThread(RunNext);
        return completion.Task;

        void RunNext()
        {
            try
            {
                steps.Dequeue()();
                // Child-handler selection refresh is deferred until MAUI finishes updating its model.
                if (steps.Count > 0)
                    NSApplication.SharedApplication.BeginInvokeOnMainThread(RunNext);
                else
                    completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
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

    void Require(bool condition, string message) => _context.Assert(condition, message);

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

    void Capture(NSView view, string name) => _context.Capture(view, $"{name}.png");

    protected override void Prepare(RuntimeTestContext context)
    {
        MacOSShell.SetUseNativeSidebar(TestShell, true);
        TestShell.Items.Add(CreateItem(true, 6));
    }

    public override Window CreateWindow(IActivationState? activationState) =>
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
                            return new CountingPage
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

sealed class CountingPageHandler : Handlers.ContentPageHandler
{
    protected override MacOSContainerView CreatePlatformView()
    {
        var page = (CountingPage)VirtualView;
        page.NativeViewCreations++;
        return new CountingPageView
        {
            Attached = () =>
            {
                page.NativeAttachments++;
                page.NativeAttachmentTraces.Add(Environment.StackTrace);
            },
        };
    }
}

sealed class CountingPage : ContentPage
{
    // ToMacOSPlatform can recreate the native view, so count across handlers for the same page.
    public int NativeAttachments { get; set; }
    public int NativeViewCreations { get; set; }
    public List<string> NativeAttachmentTraces { get; } = new();
}

sealed class CountingPageView : MacOSContainerView
{
    public Action? Attached { get; init; }

    public override void ViewDidMoveToSuperview()
    {
        base.ViewDidMoveToSuperview();
        if (Superview != null)
            Attached?.Invoke();
    }
}
