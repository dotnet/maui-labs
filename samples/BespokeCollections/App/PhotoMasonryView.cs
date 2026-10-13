using BespokeCollections.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;

namespace BespokeCollections.App;

public interface IPhotoMasonryNative
{
    NativeMasonrySnapshot Inspect();
    void ScrollToOffset(double offset);
}

public sealed class PhotoMasonryView(PhotoFeed feed, ThumbnailBytes thumbnails, ILogger logger) : View
{
    public PhotoFeed Feed { get; } = feed;
    public ThumbnailBytes Thumbnails { get; } = thumbnails;
    public ILogger Logger { get; } = logger;
    public PrototypeCounters Counters { get; } = new();
    public bool AutomaticLoadingEnabled { get; set; } = true;

    public NativeMasonrySnapshot Inspect() =>
        Handler is IPhotoMasonryNative native ? native.Inspect() :
            throw new InvalidOperationException("The native masonry handler is not connected.");

    public void ScrollToOffset(double offset)
    {
        if (!double.IsFinite(offset) || offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        Handler?.Invoke("ScrollToOffset", offset);
    }

    public async Task DemandNextAsync()
    {
        var state = Feed.State;
        if (!AutomaticLoadingEnabled || !state.HasMore || state.IsLoading || state.Error is not null)
            return;
        try
        {
            await Feed.LoadNextAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Masonry page loading failed; the feed exposes its error state.");
        }
    }
}

public partial class PhotoMasonryHandler
{
    public static readonly IPropertyMapper<PhotoMasonryView, PhotoMasonryHandler> Mapper =
        new PropertyMapper<PhotoMasonryView, PhotoMasonryHandler>(ViewHandler.ViewMapper);

    public static readonly CommandMapper<PhotoMasonryView, PhotoMasonryHandler> Commands =
        new(ViewHandler.ViewCommandMapper)
        {
            ["ScrollToOffset"] = (handler, _, offset) => handler.ScrollToOffset((double)offset!)
        };

    public PhotoMasonryHandler() : base(Mapper, Commands) { }
}
