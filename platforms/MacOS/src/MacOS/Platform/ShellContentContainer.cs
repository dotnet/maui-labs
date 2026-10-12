using AppKit;
using CoreGraphics;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

internal sealed class ShellContentContainer : NSView
{
    readonly NSScrollView _tabScroll;
    readonly NSView _tabDocument = new FlippedDocumentView();
    readonly NSView _content;

    public NSSegmentedControl Tabs { get; } = new()
    {
        SegmentStyle = NSSegmentStyle.Automatic,
        TrackingMode = NSSegmentSwitchTracking.SelectOne,
    };

    public override bool IsFlipped => true;

    public ShellContentContainer(NSView content)
    {
        _content = content;
        _tabDocument.AddSubview(Tabs);
        _tabScroll = new NSScrollView
        {
            DocumentView = _tabDocument,
            HasHorizontalScroller = true,
            HasVerticalScroller = false,
            AutohidesScrollers = true,
            DrawsBackground = false,
            Hidden = true,
        };
        AddSubview(_tabScroll);
        AddSubview(content);
    }

    public void UpdateTabs(ShellTabBarCoordinator coordinator)
    {
        Tabs.SegmentCount = coordinator.Sections.Count;
        for (var i = 0; i < coordinator.Sections.Count; i++)
        {
            var section = coordinator.Sections[i];
            Tabs.SetLabel(section.Title ?? section.CurrentItem?.Title ?? "Tab", i);
            Tabs.SetEnabled(section.IsEnabled, i);
            Tabs.SetWidth(0, i);
        }
        Tabs.SelectedSegment = coordinator.SelectedIndex;
        _tabScroll.Hidden = !coordinator.IsVisible;
        NeedsLayout = true;
    }

    public override void Layout()
    {
        try
        {
            base.Layout();
            var width = Math.Max(0, (double)Bounds.Width);
            var height = Math.Max(0, (double)Bounds.Height);
            var top = 0d;
            if (!_tabScroll.Hidden)
            {
                // The split item extends under the titlebar. Keep tabs inside the safe area.
                var safeTop = Math.Max(0, (double)SafeAreaInsets.Top);
                var size = Tabs.FittingSize;
                var tabWidth = Math.Max(0, (double)size.Width);
                var tabHeight = Math.Max(24, (double)size.Height);
                var stripHeight = tabHeight + 24;
                _tabScroll.Frame = new CGRect(0, safeTop, width, stripHeight);
                _tabDocument.Frame = new CGRect(0, 0, Math.Max(width, tabWidth + 16), tabHeight + 12);
                Tabs.Frame = new CGRect(Math.Max(8, (width - tabWidth) / 2), 6, tabWidth, tabHeight);
                top = Math.Min(height, safeTop + stripHeight);
            }
            _content.SetLayoutFrame(new CGRect(0, top, width, Math.Max(0, height - top)));
        }
        catch (Exception ex)
        {
            // Do not let managed exceptions escape AppKit's layout callback.
            Console.Error.WriteLine($"[ShellContentContainer.Layout] {ex}");
        }
    }
}
