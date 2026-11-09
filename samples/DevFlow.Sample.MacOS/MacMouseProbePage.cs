using AppKit;
using CoreGraphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace DevFlow.Sample;

public sealed class MacMouseProbePage : ContentPage
{
    public MacMouseProbePage()
    {
        var status = new Label
        {
            AutomationId = "MacMouseStatus",
            Text = "mouse: down=0 dragged=0 up=0 dx=0 dy=0",
        };
        Content = new VerticalStackLayout
        {
            Padding = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new Label { Text = "AppKit native mouse input probe" },
                new MacMouseProbeView(status)
                {
                    AutomationId = "MacMouseTarget",
                    HeightRequest = 180,
                    WidthRequest = 320,
                    BackgroundColor = Colors.LightBlue,
                },
                status,
            },
        };
    }
}

public sealed class MacMouseProbeView(Label status) : View
{
    private int downCount;
    private int dragCount;
    private int upCount;
    private CGPoint start;

    internal void Record(string phase, CGPoint point)
    {
        switch (phase)
        {
            case "down":
                downCount++;
                start = point;
                break;
            case "dragged":
                dragCount++;
                break;
            case "up":
                upCount++;
                break;
        }

        status.Text = $"mouse: down={downCount} dragged={dragCount} up={upCount} " +
            $"dx={point.X - start.X:0} dy={point.Y - start.Y:0}";
    }
}

public sealed class MacMouseProbeHandler : MacOSViewHandler<MacMouseProbeView, MacMouseProbeNativeView>
{
    public MacMouseProbeHandler() : base(ViewMapper) { }

    protected override MacMouseProbeNativeView CreatePlatformView() => new();

    protected override void ConnectHandler(MacMouseProbeNativeView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Record = VirtualView.Record;
    }

    protected override void DisconnectHandler(MacMouseProbeNativeView platformView)
    {
        platformView.Record = null;
        base.DisconnectHandler(platformView);
    }
}

public sealed class MacMouseProbeNativeView : NSView
{
    public override bool IsFlipped => true;

    public Action<string, CGPoint>? Record { get; set; }

    public override void MouseDown(NSEvent theEvent) => Report("down", theEvent);

    public override void MouseDragged(NSEvent theEvent) => Report("dragged", theEvent);

    public override void MouseUp(NSEvent theEvent) => Report("up", theEvent);

    private void Report(string phase, NSEvent theEvent) =>
        Record?.Invoke(phase, ConvertPointFromView(theEvent.LocationInWindow, null));
}
