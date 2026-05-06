using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ActivityIndicatorHandler : MacOSViewHandler<IActivityIndicator, NSProgressIndicator>
{
    public static readonly IPropertyMapper<IActivityIndicator, ActivityIndicatorHandler> Mapper =
        new PropertyMapper<IActivityIndicator, ActivityIndicatorHandler>(ViewMapper)
        {
            [nameof(IActivityIndicator.IsRunning)] = MapIsRunning,
            [nameof(IActivityIndicator.Color)] = MapColor,
        };

    public ActivityIndicatorHandler() : base(Mapper)
    {
    }

    protected override NSProgressIndicator CreatePlatformView()
    {
        return new NSProgressIndicator
        {
            Style = NSProgressIndicatorStyle.Spinning,
            IsDisplayedWhenStopped = false,
            ControlSize = NSControlSize.Regular,
        };
    }

    public static void MapIsRunning(ActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
    {
        if (activityIndicator.IsRunning)
            handler.PlatformView.StartAnimation(null);
        else
            handler.PlatformView.StopAnimation(null);
    }

    public static void MapColor(ActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
    {
        if (activityIndicator.Color != null)
        {
            handler.PlatformView.WantsLayer = true;
            var filter = new CoreImage.CIColorMonochrome();
            filter.Color = CoreImage.CIColor.FromCGColor(activityIndicator.Color.ToPlatformColor().CGColor);
            filter.Intensity = 1.0f;
            handler.PlatformView.ContentFilters = new CoreImage.CIFilter[] { filter };
        }
        else
        {
            handler.PlatformView.ContentFilters = Array.Empty<CoreImage.CIFilter>();
        }
    }
}
