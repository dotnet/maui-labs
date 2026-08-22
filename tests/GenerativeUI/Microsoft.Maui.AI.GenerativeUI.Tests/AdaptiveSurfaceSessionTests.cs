using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class AdaptiveSurfaceSessionTests
{
    [Fact]
    public void Factory_IsolatesSurfaceInstancesAndDisposesReleasedSession()
    {
        using var factory = new AdaptiveSurfaceSessionFactory();
        var first = factory.Create(
            "product:first",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());
        var second = factory.Create(
            "product:second",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());

        first.StateRoot["product"]["name"].Value = "First";
        second.StateRoot["product"]["name"].Value = "Second";

        Assert.NotSame(first.StateRoot, second.StateRoot);
        Assert.Equal("First", first.StateRoot["product"]["name"].AsString());
        Assert.Equal("Second", second.StateRoot["product"]["name"].AsString());
        Assert.True(factory.Release("product:first"));
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void GenerationTokens_RejectStaleWorkAcrossSuspendAndNewGeneration()
    {
        using var session = new AdaptiveSurfaceSession(
            "product:first",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());

        var first = session.BeginGeneration();
        var second = session.BeginGeneration();
        Assert.False(session.IsCurrentGeneration(first));
        Assert.True(session.IsCurrentGeneration(second));

        session.Suspend();
        Assert.False(session.IsCurrentGeneration(second));
        Assert.Throws<InvalidOperationException>(() => session.BeginGeneration());
    }

    [Fact]
    public void SetStandardLayout_UpdatesFallbackForSameSurface()
    {
        using var session = new AdaptiveSurfaceSession(
            "product:first",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());
        var replacement = AdaptiveCompositionTestCatalog.StandardLayout() with
        {
            LayoutId = "replacement",
        };

        session.SetStandardLayout(replacement);

        Assert.Same(replacement, session.StandardLayout);
        Assert.Throws<ArgumentException>(() => session.SetStandardLayout(
            replacement with { Surface = "Other" }));
    }
}
