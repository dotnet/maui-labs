using System.Collections.Concurrent;
using Microsoft.Maui.AI.GenerativeUI.Binding;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// Describes an application-owned surface and the named regions AI may arrange.
/// </summary>
public sealed record AdaptiveSurfaceDescriptor
{
    public required string Surface { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<AdaptiveRegionDescriptor> Regions { get; init; }

    public int MaxNodes { get; init; } = 80;

    public int MaxDepth { get; init; } = 8;
}

public sealed record AdaptiveRegionDescriptor
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public bool IsRequired { get; init; } = true;
}

/// <summary>
/// Describes one typed data object projected into the adaptive state tree.
/// </summary>
public sealed record AdaptiveDataDescriptor
{
    public required string Path { get; init; }

    public required string Contract { get; init; }

    public required string Description { get; init; }

    public bool Available { get; init; } = true;

    public string? UnavailableReason { get; init; }
}

/// <summary>
/// A complete model-visible component catalog entry.
/// </summary>
public sealed record AdaptiveComponentCatalogEntry
{
    public required string Alias { get; init; }

    public required string Description { get; init; }

    public string? WhenNotToUse { get; init; }

    public required string DataContract { get; init; }

    public required IReadOnlyList<string> RequiredBindings { get; init; }

    public required IReadOnlyList<string> OptionalBindings { get; init; }

    public required IReadOnlyList<string> Variants { get; init; }

    public required IReadOnlyList<string> AllowedRegions { get; init; }

    public required IReadOnlyList<string> CompatibleDataPaths { get; init; }

    public required bool Available { get; init; }

    public string? UnavailableReason { get; init; }
}

public sealed record AdaptiveViewportContext
{
    public required double Width { get; init; }

    public required double Height { get; init; }

    public required double Density { get; init; }

    public required string Idiom { get; init; }

    public required string Orientation { get; init; }
}

/// <summary>
/// Complete model-visible context for one adaptive surface composition.
/// </summary>
public sealed record AdaptiveSurfaceContext
{
    public required string SurfaceInstanceId { get; init; }

    public required AdaptiveSurfaceDescriptor Surface { get; init; }

    public required IReadOnlyList<AdaptiveDataDescriptor> DataManifest { get; init; }

    public required IReadOnlyList<AdaptiveComponentCatalogEntry> ComponentCatalog { get; init; }

    public required AdaptiveViewportContext Viewport { get; init; }

    public string? Intent { get; init; }

    public IReadOnlyList<string> RecentContext { get; init; } = [];

    public string StateSignature { get; init; } = string.Empty;
}

public sealed record AdaptiveSurfaceCompositionRequest(
    AdaptiveSurfaceContext Context,
    ComponentLayoutDocument StandardLayout,
    ComponentLayoutDocument? CurrentLayout,
    ComponentLayoutDocument? InvalidLayout,
    string ExpectedLayoutId,
    int ExpectedRevision,
    string? CorrectionErrors);

public sealed record AdaptiveLayoutGenerationResult(
    ComponentLayoutDocument? Layout,
    TimeSpan Duration,
    long? InputTokens,
    long? OutputTokens);

public interface IAdaptiveLayoutGenerator
{
    Task<AdaptiveLayoutGenerationResult> GenerateAsync(
        AdaptiveSurfaceCompositionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAdaptiveStandardLayoutProvider
{
    ComponentLayoutDocument GetStandardLayout(string surface);
}

public enum AdaptiveCompositionSource
{
    Generated,
    Corrected,
    Cache,
    StandardLayout,
}

public sealed record AdaptiveCompositionResult(
    ComponentLayoutDocument Layout,
    AdaptiveCompositionSource Source,
    ComponentLayoutValidationResult Validation,
    int CorrectionCount,
    TimeSpan Duration,
    long? InputTokens,
    long? OutputTokens,
    long Generation);

public sealed record AdaptiveLayoutCacheKey(
    string Surface,
    string StateSignature,
    string Intent,
    int WidthBucket,
    int HeightBucket)
{
    public static AdaptiveLayoutCacheKey Create(AdaptiveSurfaceContext context)
        => new(
            context.Surface.Surface,
            context.StateSignature,
            context.Intent?.Trim() ?? string.Empty,
            Bucket(context.Viewport.Width),
            Bucket(context.Viewport.Height));

    private static int Bucket(double value) => Math.Max(0, (int)(value / 200));
}

public interface IAdaptiveLayoutCache
{
    bool TryGet(AdaptiveLayoutCacheKey key, out ComponentLayoutDocument layout);

    void Set(AdaptiveLayoutCacheKey key, ComponentLayoutDocument layout);

    void InvalidateSurface(string surface);
}

public sealed class AdaptiveLayoutCache : IAdaptiveLayoutCache
{
    private readonly ConcurrentDictionary<AdaptiveLayoutCacheKey, ComponentLayoutDocument> _layouts = new();

    public bool TryGet(AdaptiveLayoutCacheKey key, out ComponentLayoutDocument layout)
        => _layouts.TryGetValue(key, out layout!);

    public void Set(AdaptiveLayoutCacheKey key, ComponentLayoutDocument layout)
        => _layouts[key] = layout;

    public void InvalidateSurface(string surface)
    {
        foreach (var key in _layouts.Keys
                     .Where(key => string.Equals(key.Surface, surface, StringComparison.Ordinal))
                     .ToArray())
        {
            _layouts.TryRemove(key, out _);
        }
    }
}

public interface IAdaptiveSurfaceSessionFactory
{
    AdaptiveSurfaceSession Create(
        string surfaceInstanceId,
        string surface,
        ComponentLayoutDocument standardLayout);

    bool TryGet(string surfaceInstanceId, out AdaptiveSurfaceSession session);

    bool Release(string surfaceInstanceId);
}

public sealed class AdaptiveSurfaceSessionFactory : IAdaptiveSurfaceSessionFactory, IDisposable
{
    private readonly Dictionary<string, AdaptiveSurfaceSession> _sessions = new(StringComparer.Ordinal);

    public AdaptiveSurfaceSession Create(
        string surfaceInstanceId,
        string surface,
        ComponentLayoutDocument standardLayout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        ArgumentNullException.ThrowIfNull(standardLayout);

        if (_sessions.ContainsKey(surfaceInstanceId))
            throw new InvalidOperationException($"Adaptive surface instance '{surfaceInstanceId}' already exists.");

        var session = new AdaptiveSurfaceSession(surfaceInstanceId, surface, standardLayout);
        _sessions.Add(surfaceInstanceId, session);
        return session;
    }

    public bool TryGet(string surfaceInstanceId, out AdaptiveSurfaceSession session)
        => _sessions.TryGetValue(surfaceInstanceId, out session!);

    public bool Release(string surfaceInstanceId)
    {
        if (!_sessions.Remove(surfaceInstanceId, out var session))
            return false;

        session.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();

        _sessions.Clear();
    }
}
