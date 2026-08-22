using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// Owns projected state, mounted native views, layout history, and generation ordering for one surface instance.
/// </summary>
public sealed class AdaptiveSurfaceSession : IDisposable
{
    private readonly Dictionary<string, AdaptiveRegionView> _regionHosts = new(StringComparer.OrdinalIgnoreCase);
    private long _generation;
    private bool _disposed;

    internal Dictionary<string, MountedAdaptiveNode> MountedNodes { get; } = new(StringComparer.Ordinal);

    public AdaptiveSurfaceSession(
        string surfaceInstanceId,
        string surface,
        ComponentLayoutDocument standardLayout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        ArgumentNullException.ThrowIfNull(standardLayout);
        if (!string.Equals(surface, standardLayout.Surface, StringComparison.Ordinal))
            throw new ArgumentException("The standard layout must target the session surface.", nameof(standardLayout));

        SurfaceInstanceId = surfaceInstanceId;
        Surface = surface;
        StandardLayout = standardLayout;
    }

    public string SurfaceInstanceId { get; }

    public string Surface { get; }

    public UiObject StateRoot { get; } = new();

    public ComponentLayoutDocument StandardLayout { get; private set; }

    public ComponentLayoutDocument? CurrentLayout { get; internal set; }

    public long StateVersion { get; private set; }

    public bool IsSuspended { get; private set; }

    public bool IsDisposed => _disposed;

    public bool IsStandardLayout { get; internal set; } = true;

    public void SetStandardLayout(ComponentLayoutDocument standardLayout)
    {
        ArgumentNullException.ThrowIfNull(standardLayout);
        ThrowIfDisposed();
        if (!string.Equals(Surface, standardLayout.Surface, StringComparison.Ordinal))
            throw new ArgumentException("The standard layout must target the session surface.", nameof(standardLayout));

        StandardLayout = standardLayout;
    }

    public long BeginGeneration()
    {
        ThrowIfDisposed();
        if (IsSuspended)
            throw new InvalidOperationException("A suspended adaptive surface cannot begin generation.");
        return Interlocked.Increment(ref _generation);
    }

    public bool IsCurrentGeneration(long generation)
        => !_disposed && generation == Volatile.Read(ref _generation);

    public void CancelPendingGeneration()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _generation);
    }

    public void Suspend()
    {
        ThrowIfDisposed();
        IsSuspended = true;
        CancelPendingGeneration();
    }

    public void Resume()
    {
        ThrowIfDisposed();
        IsSuspended = false;
    }

    public View? GetMountedView(string nodeId)
        => MountedNodes.TryGetValue(nodeId, out var mounted) ? mounted.View : null;

    internal void RegisterRegionHost(AdaptiveRegionView host)
    {
        ThrowIfDisposed();
        _regionHosts[host.Region] = host;
        if (CurrentLayout?.Regions.FirstOrDefault(region =>
                string.Equals(region.Region, host.Region, StringComparison.OrdinalIgnoreCase)) is { } plan &&
            MountedNodes.TryGetValue(plan.RootNodeId, out var mounted))
        {
            host.SetAdaptiveContent(mounted.View);
        }
    }

    internal bool TryGetRegionHost(string region, out AdaptiveRegionView host)
        => _regionHosts.TryGetValue(region, out host!);

    internal void ClearRegionHosts()
    {
        foreach (var host in _regionHosts.Values)
            host.SetAdaptiveContent(null);
    }

    internal IReadOnlyList<AdaptiveRegionView> RegionHosts => [.. _regionHosts.Values];

    internal void NotifyStateProjected() => StateVersion++;

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Increment(ref _generation);
        foreach (var mounted in MountedNodes.Values)
        {
            if (mounted.View is ICompositionComponent component)
                component.Detach();
        }

        foreach (var host in _regionHosts.Values)
            host.SetAdaptiveContent(null);

        MountedNodes.Clear();
        _regionHosts.Clear();
        CurrentLayout = null;
    }
}

internal sealed record MountedAdaptiveNode(
    ComponentLayoutNode Node,
    View View);
