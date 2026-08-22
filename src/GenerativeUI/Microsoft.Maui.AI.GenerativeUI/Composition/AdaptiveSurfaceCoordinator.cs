using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public sealed record PresentationIntentContext(
    string? Intent,
    IReadOnlyList<string> RecentUserContext);

public sealed record AdaptiveSurfaceStatus(
    string SurfaceInstanceId,
    bool IsComposing,
    bool IsAdapted,
    string? Intent,
    string? Explanation,
    string? Error);

public sealed class AdaptiveSurfaceStatusChangedEventArgs(AdaptiveSurfaceStatus status) : EventArgs
{
    public AdaptiveSurfaceStatus Status { get; } = status;
}

public interface IAdaptiveSurface
{
    AdaptiveSurfaceSession Session { get; }

    ValueTask<AdaptiveSurfaceContext> CreateContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken);
}

public interface IAdaptiveSurfaceDispatcher
{
    Task DispatchAsync(Func<Task> action);

    Task<T> DispatchAsync<T>(Func<Task<T>> action);
}

public interface IAdaptiveSurfaceTransition
{
    Task AnimateAsync(
        AdaptiveSurfaceSession session,
        CancellationToken cancellationToken);
}

public sealed class MauiAdaptiveSurfaceDispatcher : IAdaptiveSurfaceDispatcher
{
    public Task DispatchAsync(Func<Task> action)
        => MainThread.InvokeOnMainThreadAsync(action);

    public Task<T> DispatchAsync<T>(Func<Task<T>> action)
        => MainThread.InvokeOnMainThreadAsync(action);
}

public sealed class MauiAdaptiveSurfaceTransition : IAdaptiveSurfaceTransition
{
    public async Task AnimateAsync(
        AdaptiveSurfaceSession session,
        CancellationToken cancellationToken)
    {
        foreach (var host in session.RegionHosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            host.Opacity = 0.82;
            await host.FadeToAsync(1, 140, Easing.CubicOut);
        }
    }
}

/// <summary>
/// Coordinates active-surface lifetime and automatic intent-driven composition without exposing a compose tool.
/// </summary>
public sealed class AdaptiveSurfaceCoordinator(
    AdaptiveSurfaceComposer composer,
    AdaptiveRegionRenderer renderer,
    IAdaptiveSurfaceDispatcher dispatcher,
    IAdaptiveSurfaceTransition transition) : IDisposable
{
    private static readonly TimeSpan IntentDebounce = TimeSpan.FromMilliseconds(250);
    private readonly object _gate = new();
    private readonly Queue<string> _recentContext = new();
    private IAdaptiveSurface? _activeSurface;
    private CancellationTokenSource? _compositionCancellation;
    private Task _pendingComposition = Task.CompletedTask;
    private string? _intent;
    private bool _disposed;

    public event EventHandler<AdaptiveSurfaceStatusChangedEventArgs>? StatusChanged;

    public string? LatestIntent
    {
        get
        {
            lock (_gate)
                return _intent;
        }
    }

    public AdaptiveSurfaceStatus? CurrentStatus { get; private set; }

    public async Task ActivateAsync(
        IAdaptiveSurface surface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ThrowIfDisposed();

        IAdaptiveSurface? previous;
        lock (_gate)
        {
            previous = _activeSurface;
            _activeSurface = surface;
            CancelCompositionLocked();
        }

        if (previous is not null && !ReferenceEquals(previous, surface) && !previous.Session.IsDisposed)
            previous.Session.Suspend();

        surface.Session.Resume();
        if (surface.Session.CurrentLayout is null)
        {
            await dispatcher.DispatchAsync(() =>
            {
                renderer.Render(surface.Session.StandardLayout, surface.Session);
                surface.Session.IsStandardLayout = true;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        ScheduleComposition(surface, TimeSpan.Zero, cancellationToken);
    }

    public void Deactivate(IAdaptiveSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (_disposed)
            return;

        lock (_gate)
        {
            if (!ReferenceEquals(_activeSurface, surface))
                return;

            CancelCompositionLocked();
            _activeSurface = null;
        }

        if (!surface.Session.IsDisposed)
            surface.Session.Suspend();
    }

    public void PublishIntent(string text)
    {
        ThrowIfDisposed();
        var normalized = NormalizeIntent(text);
        if (normalized.Length == 0)
            return;

        IAdaptiveSurface? active;
        lock (_gate)
        {
            _intent = normalized;
            _recentContext.Enqueue(normalized);
            while (_recentContext.Count > 4)
                _recentContext.Dequeue();
            active = _activeSurface;
        }

        if (active is not null)
            ScheduleComposition(active, IntentDebounce, CancellationToken.None);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IAdaptiveSurface? active;
        lock (_gate)
            active = _activeSurface;

        return active is null
            ? Task.CompletedTask
            : RefreshAsync(active, cancellationToken);
    }

    public Task RefreshAsync(
        IAdaptiveSurface surface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ThrowIfDisposed();
        lock (_gate)
        {
            if (!ReferenceEquals(_activeSurface, surface))
                return Task.CompletedTask;
        }

        ScheduleComposition(surface, TimeSpan.Zero, cancellationToken);
        return WhenIdleAsync();
    }

    public async Task ResetToStandardAsync()
    {
        ThrowIfDisposed();
        IAdaptiveSurface? active;
        lock (_gate)
        {
            _intent = null;
            _recentContext.Clear();
            active = _activeSurface;
            CancelCompositionLocked();
        }

        if (active is null || active.Session.IsDisposed)
            return;

        active.Session.CancelPendingGeneration();
        var current = active.Session.CurrentLayout;
        var standard = active.Session.StandardLayout with
        {
            LayoutId = current?.LayoutId ?? active.Session.StandardLayout.LayoutId,
            Revision = (current?.Revision ?? 0) + 1,
        };
        await dispatcher.DispatchAsync(() =>
        {
            renderer.Render(standard, active.Session);
            active.Session.IsStandardLayout = true;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        PublishStatus(new(
            active.Session.SurfaceInstanceId,
            IsComposing: false,
            IsAdapted: false,
            Intent: null,
            Explanation: null,
            Error: null));
    }

    public Task WhenIdleAsync()
    {
        lock (_gate)
            return _pendingComposition;
    }

    private void ScheduleComposition(
        IAdaptiveSurface surface,
        TimeSpan delay,
        CancellationToken externalCancellation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_activeSurface, surface) || surface.Session.IsDisposed)
                return;

            CancelCompositionLocked();
            _compositionCancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
            _pendingComposition = ComposeAsync(surface, delay, _compositionCancellation.Token);
        }
    }

    private async Task ComposeAsync(
        IAdaptiveSurface surface,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await dispatcher.DispatchAsync(() =>
            {
                EnsureActive(surface, cancellationToken);
                PublishStatus(new(
                    surface.Session.SurfaceInstanceId,
                    IsComposing: true,
                    IsAdapted: !surface.Session.IsStandardLayout,
                    Intent: LatestIntent,
                    Explanation: surface.Session.CurrentLayout?.Explanation,
                    Error: null));
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            var presentation = SnapshotPresentation();
            var context = await dispatcher.DispatchAsync(() =>
                surface.CreateContextAsync(presentation, cancellationToken).AsTask()).ConfigureAwait(false);
            EnsureActive(surface, cancellationToken);
            var result = await composer.ComposeAsync(context, surface.Session, cancellationToken).ConfigureAwait(false);
            EnsureActive(surface, cancellationToken);

            await dispatcher.DispatchAsync(async () =>
            {
                EnsureActive(surface, cancellationToken);
                if (result.Source != AdaptiveCompositionSource.CurrentLayout)
                {
                    renderer.Render(result, surface.Session);
                    surface.Session.IsStandardLayout =
                        result.Source == AdaptiveCompositionSource.StandardLayout;
                    await transition.AnimateAsync(surface.Session, cancellationToken);
                }

                PublishStatus(new(
                    surface.Session.SurfaceInstanceId,
                    IsComposing: false,
                    IsAdapted: !surface.Session.IsStandardLayout,
                    Intent: presentation.Intent,
                    Explanation: result.Layout.Explanation,
                    Error: null));
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsActive(surface))
            {
                await dispatcher.DispatchAsync(() =>
                {
                    if (IsActive(surface))
                    {
                        PublishStatus(new(
                            surface.Session.SurfaceInstanceId,
                            IsComposing: false,
                            IsAdapted: !surface.Session.IsStandardLayout,
                            Intent: SnapshotPresentation().Intent,
                            Explanation: surface.Session.CurrentLayout?.Explanation,
                            Error: ex.Message));
                    }

                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }
        }
    }

    private PresentationIntentContext SnapshotPresentation()
    {
        lock (_gate)
            return new(_intent, [.. _recentContext]);
    }

    private bool IsActive(IAdaptiveSurface surface)
    {
        lock (_gate)
            return ReferenceEquals(_activeSurface, surface) && !surface.Session.IsDisposed;
    }

    private void EnsureActive(IAdaptiveSurface surface, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsActive(surface))
            throw new OperationCanceledException("The adaptive surface is no longer active.");
    }

    private void PublishStatus(AdaptiveSurfaceStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(this, new(status));
    }

    private void CancelCompositionLocked()
    {
        _compositionCancellation?.Cancel();
        _compositionCancellation?.Dispose();
        _compositionCancellation = null;
    }

    private static string NormalizeIntent(string text)
        => string.Join(
            ' ',
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            CancelCompositionLocked();
            _activeSurface = null;
            _recentContext.Clear();
        }
    }
}
