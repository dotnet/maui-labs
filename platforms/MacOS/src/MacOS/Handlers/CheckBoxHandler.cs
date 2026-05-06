using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class CheckBoxHandler : MacOSViewHandler<ICheckBox, NSButton>
{
    public static readonly IPropertyMapper<ICheckBox, CheckBoxHandler> Mapper =
        new PropertyMapper<ICheckBox, CheckBoxHandler>(ViewMapper)
        {
            [nameof(ICheckBox.IsChecked)] = MapIsChecked,
            [nameof(ICheckBox.Foreground)] = MapForeground,
        };

    bool _updating;

    public CheckBoxHandler() : base(Mapper)
    {
    }

    protected override NSButton CreatePlatformView()
    {
        var button = new NSButton
        {
            Title = string.Empty,
        };
        button.SetButtonType(NSButtonType.Switch);
        return button;
    }

    protected override void ConnectHandler(NSButton platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Activated += OnActivated;
    }

    protected override void DisconnectHandler(NSButton platformView)
    {
        platformView.Activated -= OnActivated;
        base.DisconnectHandler(platformView);
    }

    void OnActivated(object? sender, EventArgs e)
    {
        if (_updating || VirtualView == null)
            return;

        _updating = true;
        try
        {
            VirtualView.IsChecked = PlatformView.State == NSCellStateValue.On;
        }
        finally
        {
            _updating = false;
        }
    }

    public static void MapIsChecked(CheckBoxHandler handler, ICheckBox view)
    {
        if (handler._updating)
            return;

        handler.PlatformView.State = view.IsChecked ? NSCellStateValue.On : NSCellStateValue.Off;
    }

    public static void MapForeground(CheckBoxHandler handler, ICheckBox view)
    {
        if (view.Foreground is Graphics.SolidPaint solidPaint && solidPaint.Color != null)
            handler.PlatformView.ContentTintColor = solidPaint.Color.ToPlatformColor();
    }
}
