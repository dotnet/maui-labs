using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public class ProgressBarHandler : MacOSViewHandler<IProgress, NSProgressIndicator>
{
    public static readonly IPropertyMapper<IProgress, ProgressBarHandler> Mapper =
        new PropertyMapper<IProgress, ProgressBarHandler>(ViewMapper)
        {
            [nameof(IProgress.Progress)] = MapProgress,
            [nameof(IProgress.ProgressColor)] = MapProgressColor,
        };

    public ProgressBarHandler() : base(Mapper) { }

    protected override NSProgressIndicator CreatePlatformView()
    {
        return new NSProgressIndicator
        {
            Style = NSProgressIndicatorStyle.Bar,
            Indeterminate = false,
            MinValue = 0,
            MaxValue = 1,
            DoubleValue = 0,
        };
    }

    public static void MapProgress(ProgressBarHandler handler, IProgress progress)
    {
        handler.PlatformView.DoubleValue = Math.Clamp(progress.Progress, 0, 1);
    }

    public static void MapProgressColor(ProgressBarHandler handler, IProgress progress)
    {
        if (progress.ProgressColor != null)
        {
            // NSProgressIndicator uses the system accent color by default.
            // We can tint it by adding a colored sublayer over the track.
            handler.PlatformView.WantsLayer = true;
            if (handler.PlatformView.Layer != null)
            {
                // Use content filters to colorize
                var filter = new CoreImage.CIColorMonochrome();
                filter.Color = CoreImage.CIColor.FromCGColor(progress.ProgressColor.ToPlatformColor().CGColor);
                filter.Intensity = 1.0f;
                handler.PlatformView.ContentFilters = new CoreImage.CIFilter[] { filter };
            }
        }
        else
        {
            handler.PlatformView.ContentFilters = Array.Empty<CoreImage.CIFilter>();
        }
    }
}
