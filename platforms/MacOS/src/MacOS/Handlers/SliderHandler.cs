using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class SliderHandler : MacOSViewHandler<ISlider, NSSlider>
{
    public static readonly IPropertyMapper<ISlider, SliderHandler> Mapper =
        new PropertyMapper<ISlider, SliderHandler>(ViewMapper)
        {
            [nameof(IRange.Minimum)] = MapMinimum,
            [nameof(IRange.Maximum)] = MapMaximum,
            [nameof(IRange.Value)] = MapValue,
            [nameof(ISlider.MinimumTrackColor)] = MapMinimumTrackColor,
            [nameof(ISlider.MaximumTrackColor)] = MapMaximumTrackColor,
            [nameof(ISlider.ThumbColor)] = MapThumbColor,
        };

    bool _updating;

    public SliderHandler() : base(Mapper)
    {
    }

    protected override NSSlider CreatePlatformView()
    {
        var slider = new NSSlider
        {
            MinValue = 0,
            MaxValue = 100,
            DoubleValue = 50,
        };
        return slider;
    }

    protected override void ConnectHandler(NSSlider platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Activated += OnActivated;
    }

    protected override void DisconnectHandler(NSSlider platformView)
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
            if (VirtualView is IRange range)
                range.Value = PlatformView.DoubleValue;
        }
        finally
        {
            _updating = false;
        }
    }

    public static void MapMinimum(SliderHandler handler, ISlider slider)
    {
        if (slider is IRange range)
            handler.PlatformView.MinValue = range.Minimum;
    }

    public static void MapMaximum(SliderHandler handler, ISlider slider)
    {
        if (slider is IRange range)
            handler.PlatformView.MaxValue = range.Maximum;
    }

    public static void MapValue(SliderHandler handler, ISlider slider)
    {
        if (handler._updating)
            return;

        if (slider is IRange range)
            handler.PlatformView.DoubleValue = range.Value;
    }

    public static void MapMinimumTrackColor(SliderHandler handler, ISlider slider)
    {
        // NSSlider doesn't natively support separate min track color tinting
        // A production implementation would use a custom NSSliderCell
    }

    public static void MapMaximumTrackColor(SliderHandler handler, ISlider slider)
    {
        // NSSlider doesn't natively support separate max track color
    }

    public static void MapThumbColor(SliderHandler handler, ISlider slider)
    {
        // NSSlider doesn't natively support thumb tinting without custom cell
    }
}
