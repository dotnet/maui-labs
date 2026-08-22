using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Pages;

/// <summary>
/// Owns one isolated adaptive session for the lifetime of a Shell page instance.
/// </summary>
public abstract class AdaptiveContentPage : ContentPage, IAdaptiveSurface
{
    private readonly IAdaptiveSurfaceSessionFactory _sessionFactory;
    private readonly AdaptiveSurfaceCoordinator _coordinator;
    private CancellationTokenSource? _appearanceCancellation;
    private bool _released;

    protected AdaptiveContentPage(
        IAdaptiveSurfaceSessionFactory sessionFactory,
        AdaptiveSurfaceCoordinator coordinator,
        string surface,
        ComponentLayoutDocument standardLayout)
    {
        _sessionFactory = sessionFactory;
        _coordinator = coordinator;
        Session = sessionFactory.Create(
            $"{surface}:{Guid.NewGuid():N}",
            surface,
            standardLayout);
    }

    public AdaptiveSurfaceSession Session { get; }

    protected void AttachAdaptiveRegion(AdaptiveRegionView region)
        => region.Attach(Session);

    protected Task ActivateAdaptiveSurfaceAsync(CancellationToken cancellationToken = default)
        => _coordinator.ActivateAsync(this, cancellationToken);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _appearanceCancellation?.Cancel();
        _appearanceCancellation?.Dispose();
        var cancellation = _appearanceCancellation = new();
        try
        {
            if (await PrepareAdaptiveStateAsync(cancellation.Token))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await ActivateAdaptiveSurfaceAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    protected override void OnDisappearing()
    {
        _appearanceCancellation?.Cancel();
        _coordinator.Deactivate(this);
        base.OnDisappearing();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (args.NavigationType != NavigationType.Pop || _released)
            return;

        _released = true;
        _appearanceCancellation?.Cancel();
        _coordinator.Deactivate(this);
        _sessionFactory.Release(Session.SurfaceInstanceId);
    }

    protected abstract ValueTask<AdaptiveSurfaceContext> CreateAdaptiveContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken);

    protected virtual Task<bool> PrepareAdaptiveStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    ValueTask<AdaptiveSurfaceContext> IAdaptiveSurface.CreateContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken)
        => CreateAdaptiveContextAsync(presentation, cancellationToken);

}
