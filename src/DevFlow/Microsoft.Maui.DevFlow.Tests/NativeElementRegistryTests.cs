using System.Runtime.CompilerServices;
using Microsoft.Maui.DevFlow.Agent.Native;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers the element id contract the native agent depends on: a client fetches the tree, then acts
/// on an element by id in a later request.
/// </summary>
/// <remarks>
/// These live here rather than in the integration suite because the behaviour hinges on GC timing,
/// which is nondeterministic on device — an Apple managed peer for a framework type is recreated on
/// each marshal and collectable while the native view lives on. The integration suite can only
/// observe the failure by luck; these assert it directly.
/// </remarks>
public class NativeElementRegistryTests
{
    /// <summary>Stands in for a native view. <see cref="Handle"/> models the ObjC pointer.</summary>
    private sealed class FakeView(string? handle = null)
    {
        public string? Handle { get; } = handle;
    }

    private static NativeElementRegistry CreateRegistry()
        => new(view => (view as FakeView)?.Handle is { } handle ? $"objc:{handle}" : null);

    // Kept out of the caller's frame so the only reference left is the registry's.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string Id, WeakReference Tracker) RegisterOutOfScope(NativeElementRegistry registry, string handle)
    {
        var view = new FakeView(handle);
        return (registry.Register(view, parentId: null), new WeakReference(view));
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void Resolve_AfterViewGoesOutOfScopeAndCollects_StillReturnsTheView()
    {
        var registry = CreateRegistry();

        var (id, tracker) = RegisterOutOfScope(registry, "aaa");
        Collect();

        // A weak hold would have dropped the peer here and stranded the client's id.
        Assert.True(tracker.IsAlive);
        Assert.NotNull(registry.Resolve(id));
    }

    [Fact]
    public void Register_FreshPeerAfterCollection_ReusesTheId()
    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        var (id, _) = RegisterOutOfScope(registry, "aaa");
        Collect();

        // This is the shape of ResolveView recovering from a miss: it re-walks, and the peer the
        // walk marshals for the still-live native view is a different managed instance.
        registry.BeginWalk();
        var rewalked = new FakeView("aaa");

        Assert.Equal(id, registry.Register(rewalked, parentId: null));
        Assert.Same(rewalked, registry.Resolve(id));
    }

    [Fact]
    public void Register_SameHandleViaFreshManagedPeer_ReusesTheId()    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        var first = registry.Register(new FakeView("aaa"), parentId: null);

        // A later walk marshals a brand new peer for the same native view.
        registry.BeginWalk();
        var second = registry.Register(new FakeView("aaa"), parentId: null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_AfterRewalk_ReturnsTheMostRecentlySeenPeer()
    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        var id = registry.Register(new FakeView("aaa"), parentId: null);

        registry.BeginWalk();
        var latest = new FakeView("aaa");
        registry.Register(latest, parentId: null);

        Assert.Same(latest, registry.Resolve(id));
    }

    [Fact]
    public void Register_WithoutStableKey_KeepsIdPerInstance()
    {
        var registry = CreateRegistry();
        var view = new FakeView();

        var first = registry.Register(view, parentId: null);
        var second = registry.Register(view, parentId: null);
        var other = registry.Register(new FakeView(), parentId: null);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Resolve_UnknownOrEmptyId_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.Resolve(null));
        Assert.Null(registry.Resolve(string.Empty));
        Assert.Null(registry.Resolve("n999"));
    }

    [Fact]
    public void ParentOf_TracksTheLastWalk()
    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        var id = registry.Register(new FakeView("aaa"), parentId: "n1");
        Assert.Equal("n1", registry.ParentOf(id));

        // Reparented on a later walk.
        registry.BeginWalk();
        registry.Register(new FakeView("aaa"), parentId: "n2");
        Assert.Equal("n2", registry.ParentOf(id));
    }

    [Fact]
    public void BeginWalk_EvictsElementsUnseenForMoreThanOneWalk()
    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        var stale = registry.Register(new FakeView("stale"), parentId: null);

        // Push past the 512 cap so eviction has something to do.
        registry.BeginWalk();
        for (var i = 0; i < 513; i++)
            registry.Register(new FakeView($"live{i}"), parentId: null);

        Assert.NotNull(registry.Resolve(stale));

        registry.BeginWalk();

        Assert.Null(registry.Resolve(stale));
    }

    [Fact]
    public void BeginWalk_KeepsElementsFromTheCurrentAndPreviousWalk()
    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        registry.Register(new FakeView("stale"), parentId: null);

        registry.BeginWalk();
        var recent = new List<string>();
        for (var i = 0; i < 513; i++)
            recent.Add(registry.Register(new FakeView($"live{i}"), parentId: null));

        registry.BeginWalk();

        // Over the cap, but everything here was seen in the previous walk, so none of it is evictable.
        Assert.All(recent, id => Assert.NotNull(registry.Resolve(id)));
    }

    [Fact]
    public void Register_WithoutStableKey_AfterEviction_ReusesTheIdForTheSameInstance()
    {
        // The Android path: no native handle, so ids hang off the managed instance via the
        // ConditionalWeakTable. That table outlives eviction, and it should -- an evicted view
        // that is still alive and walked again is the same element and keeps its id.
        var registry = CreateRegistry();

        registry.BeginWalk();
        var view = new FakeView();
        var original = registry.Register(view, parentId: null);

        registry.BeginWalk();
        for (var i = 0; i < 513; i++)
            registry.Register(new FakeView($"live{i}"), parentId: null);

        registry.BeginWalk();
        Assert.Null(registry.Resolve(original));

        Assert.Equal(original, registry.Register(view, parentId: null));
        Assert.Same(view, registry.Resolve(original));
    }

    [Fact]
    public void Register_HandleRecycledAfterEviction_DoesNotInheritTheOldId()
    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        var original = registry.Register(new FakeView("aaa"), parentId: null);

        registry.BeginWalk();
        for (var i = 0; i < 513; i++)
            registry.Register(new FakeView($"live{i}"), parentId: null);

        registry.BeginWalk();
        Assert.Null(registry.Resolve(original));

        // The runtime hands the freed address to an unrelated view.
        var recycled = registry.Register(new FakeView("aaa"), parentId: null);

        Assert.NotEqual(original, recycled);
        Assert.Null(registry.Resolve(original));
    }

    // CurrentWalk is what NativeDevFlowAgentService.HandleHitTest reports as the hit-test response's
    // captureEpoch (see openapi.yaml — captureEpoch has "minimum: 1"), so it must never surface as 0
    // and must actually change per walk rather than being a fixed stand-in value.
    [Fact]
    public void CurrentWalk_BeforeAnyWalk_IsZero()
    {
        var registry = CreateRegistry();

        Assert.Equal(0, registry.CurrentWalk);
    }

    [Fact]
    public void CurrentWalk_AfterBeginWalk_IsPositiveAndIncrements()
    {
        var registry = CreateRegistry();

        registry.BeginWalk();
        var first = registry.CurrentWalk;
        Assert.True(first >= 1, "captureEpoch must be >= 1 per the OpenAPI contract once a walk has run.");

        registry.BeginWalk();
        var second = registry.CurrentWalk;

        Assert.True(second > first, "Each walk (and therefore each hit-test/tree/query capture) must report a distinct epoch.");
    }
}
