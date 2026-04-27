using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class PickerHandler : MacOSViewHandler<IPicker, NSPopUpButton>
{
    public static readonly IPropertyMapper<IPicker, PickerHandler> Mapper =
        new PropertyMapper<IPicker, PickerHandler>(ViewMapper)
        {
            [nameof(IPicker.Title)] = MapTitle,
            [nameof(IPicker.TitleColor)] = MapTitleColor,
            [nameof(IPicker.SelectedIndex)] = MapSelectedIndex,
            [nameof(IPicker.Items)] = MapItems,
            [nameof(ITextStyle.TextColor)] = MapTextColor,
            [nameof(IView.Background)] = MapBackground,
        };

    public PickerHandler() : base(Mapper)
    {
    }

    protected override NSPopUpButton CreatePlatformView()
    {
        var popup = new NSPopUpButton(new CoreGraphics.CGRect(0, 0, 200, 25), false);
        popup.AutoresizesSubviews = true;
        // Ensure the popup has a menu
        if (popup.Menu == null)
            popup.Menu = new NSMenu();
        return popup;
    }

    protected override void ConnectHandler(NSPopUpButton platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Activated += OnActivated;
        RebuildItems();
    }

    protected override void DisconnectHandler(NSPopUpButton platformView)
    {
        platformView.Activated -= OnActivated;
        base.DisconnectHandler(platformView);
    }

    void OnActivated(object? sender, EventArgs e)
    {
        if (VirtualView == null)
            return;

        var selectedIndex = (int)PlatformView.IndexOfSelectedItem;
        // Account for the placeholder item at index 0
        if (VirtualView.Title != null)
        {
            if (selectedIndex == 0)
                return; // Placeholder — not a real selection
            selectedIndex -= 1;
        }

        if (selectedIndex >= 0 && selectedIndex < VirtualView.Items.Count)
            VirtualView.SelectedIndex = selectedIndex;
    }

    void RebuildItems()
    {
        if (VirtualView == null)
            return;

        // Clear existing items via menu
        var menu = PlatformView.Menu;
        if (menu == null)
        {
            menu = new NSMenu();
            PlatformView.Menu = menu;
        }
        menu.RemoveAllItems();

        // Add placeholder as a disabled, greyed-out item — visible but not selectable
        if (VirtualView.Title != null)
        {
            var placeholder = new NSMenuItem(VirtualView.Title);
            placeholder.Enabled = false;

            // Style with secondary label color so it looks like a placeholder
            var attrs = new Foundation.NSDictionary(
                NSStringAttributeKey.ForegroundColor,
                NSColor.SecondaryLabel);
            placeholder.AttributedTitle = new Foundation.NSAttributedString(VirtualView.Title, attrs);

            menu.AddItem(placeholder);
            menu.AddItem(NSMenuItem.SeparatorItem);
        }

        if (VirtualView.Items != null)
        {
            for (int i = 0; i < VirtualView.Items.Count; i++)
                menu.AddItem(new NSMenuItem(VirtualView.Items[i] ?? ""));
        }

        UpdateSelection();
    }

    void UpdateSelection()
    {
        if (VirtualView == null)
            return;

        // Offset accounts for placeholder item + separator
        var offset = VirtualView.Title != null ? 2 : 0;

        if (VirtualView.SelectedIndex >= 0 && VirtualView.SelectedIndex < VirtualView.Items.Count)
            PlatformView.SelectItem(VirtualView.SelectedIndex + offset);
        else if (VirtualView.Title != null)
            PlatformView.SelectItem(0); // Show placeholder text
    }

    public static void MapTitle(PickerHandler handler, IPicker picker)
    {
        handler.RebuildItems();
    }

    public static void MapTitleColor(PickerHandler handler, IPicker picker)
    {
        if (picker.Title == null)
            return;

        // Apply TitleColor to the placeholder item (index 0) via attributed title
        var color = picker.TitleColor?.ToPlatformColor() ?? NSColor.SecondaryLabel;
        var menu = handler.PlatformView.Menu;
        if (menu != null && menu.Count > 0)
        {
            var item = menu.ItemAt(0);
            var attrs = new Foundation.NSDictionary(
                AppKit.NSStringAttributeKey.ForegroundColor, color);
            item.AttributedTitle = new Foundation.NSAttributedString(picker.Title, attrs);
        }
    }

    public static void MapSelectedIndex(PickerHandler handler, IPicker picker)
    {
        handler.UpdateSelection();
    }

    public static void MapItems(PickerHandler handler, IPicker picker)
    {
        handler.RebuildItems();
    }

    public static void MapTextColor(PickerHandler handler, IPicker picker)
    {
        // NSPopUpButton uses system text color by default
    }

    public static void MapBackground(PickerHandler handler, IPicker picker)
    {
        if (picker.Background is Graphics.SolidPaint solidPaint && solidPaint.Color != null)
        {
            handler.PlatformView.WantsLayer = true;
            handler.PlatformView.Layer!.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
        }
    }
}
