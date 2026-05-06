using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class SwitchHandler : MacOSViewHandler<ISwitch, NSSwitch>
{
    public static readonly IPropertyMapper<ISwitch, SwitchHandler> Mapper =
        new PropertyMapper<ISwitch, SwitchHandler>(ViewMapper)
        {
            [nameof(ISwitch.IsOn)] = MapIsOn,
            [nameof(ISwitch.TrackColor)] = MapTrackColor,
            [nameof(ISwitch.ThumbColor)] = MapThumbColor,
        };

    bool _updating;

    public SwitchHandler() : base(Mapper)
    {
    }

    protected override NSSwitch CreatePlatformView()
    {
        return new NSSwitch();
    }

    protected override void ConnectHandler(NSSwitch platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Activated += OnActivated;
    }

    protected override void DisconnectHandler(NSSwitch platformView)
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
            VirtualView.IsOn = PlatformView.State == (nint)(long)NSCellStateValue.On;
        }
        finally
        {
            _updating = false;
        }
    }

    public static void MapIsOn(SwitchHandler handler, ISwitch view)
    {
        if (handler._updating)
            return;

        handler.PlatformView.State = view.IsOn ? (nint)(long)NSCellStateValue.On : (nint)(long)NSCellStateValue.Off;
    }

    public static void MapTrackColor(SwitchHandler handler, ISwitch view)
    {
        // NSSwitch doesn't expose track color customization
    }

    public static void MapThumbColor(SwitchHandler handler, ISwitch view)
    {
        // NSSwitch doesn't expose thumb color customization
    }
}
