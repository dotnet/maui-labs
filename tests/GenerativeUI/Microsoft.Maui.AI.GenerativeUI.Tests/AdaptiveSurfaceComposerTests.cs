using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class AdaptiveSurfaceComposerTests
{
    [Fact]
    public async Task Compose_InvalidThenValid_RetriesWithDeterministicCorrection()
    {
        var generator = new ScriptedGenerator(
            request => Invalid(request),
            request => AdaptiveCompositionTestCatalog.StandardLayout(
                request.ExpectedLayoutId,
                request.ExpectedRevision));
        var composer = new AdaptiveSurfaceComposer(
            generator,
            new ComponentLayoutValidator(),
            new AdaptiveLayoutCache());
        using var session = Session();

        var result = await composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), session);

        Assert.Equal(AdaptiveCompositionSource.Corrected, result.Source);
        Assert.Equal(1, result.CorrectionCount);
        Assert.Equal(2, generator.Requests.Count);
        Assert.Contains("unknown_component", generator.Requests[1].CorrectionErrors, StringComparison.Ordinal);
        Assert.NotNull(generator.Requests[1].InvalidLayout);
    }

    [Fact]
    public async Task Compose_TwoInvalidLayouts_ReturnsValidatedStandardLayout()
    {
        var generator = new ScriptedGenerator(Invalid, Invalid);
        var composer = new AdaptiveSurfaceComposer(
            generator,
            new ComponentLayoutValidator(),
            new AdaptiveLayoutCache());
        using var session = Session();

        var result = await composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), session);

        Assert.Equal(AdaptiveCompositionSource.StandardLayout, result.Source);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(2, generator.Requests.Count);
    }

    [Fact]
    public async Task Compose_Cancellation_DoesNotFallBack()
    {
        var composer = new AdaptiveSurfaceComposer(
            new CancelledGenerator(),
            new ComponentLayoutValidator(),
            new AdaptiveLayoutCache());
        using var session = Session();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), session, cancellation.Token));
    }

    [Fact]
    public async Task Compose_SameSurfaceStateIntent_UsesExactCacheKey()
    {
        var generator = new ScriptedGenerator(request =>
            AdaptiveCompositionTestCatalog.StandardLayout(request.ExpectedLayoutId, request.ExpectedRevision));
        var cache = new AdaptiveLayoutCache();
        var composer = new AdaptiveSurfaceComposer(
            generator,
            new ComponentLayoutValidator(),
            cache);

        using var first = Session();
        var generated = await composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), first);
        using var second = new AdaptiveSurfaceSession(
            "product:watering-can",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());
        var cached = await composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), second);

        Assert.Equal(AdaptiveCompositionSource.Generated, generated.Source);
        Assert.Equal(AdaptiveCompositionSource.Cache, cached.Source);
        Assert.Single(generator.Requests);
    }

    [Fact]
    public async Task Compose_DifferentRecentContext_DoesNotReuseCache()
    {
        var generator = new ScriptedGenerator(
            request => AdaptiveCompositionTestCatalog.StandardLayout(
                request.ExpectedLayoutId,
                request.ExpectedRevision),
            request => AdaptiveCompositionTestCatalog.StandardLayout(
                request.ExpectedLayoutId,
                request.ExpectedRevision));
        var composer = new AdaptiveSurfaceComposer(
            generator,
            new ComponentLayoutValidator(),
            new AdaptiveLayoutCache());
        using var first = Session();
        using var second = Session();
        var context = AdaptiveCompositionTestCatalog.Context();

        await composer.ComposeAsync(
            context with { RecentContext = ["balcony constraints"] },
            first);
        await composer.ComposeAsync(
            context with { RecentContext = ["indoor constraints"] },
            second);

        Assert.Equal(2, generator.Requests.Count);
    }

    [Fact]
    public async Task Compose_WrongInitialIdentity_RetriesThenUsesStandard()
    {
        var generator = new ScriptedGenerator(
            request => AdaptiveCompositionTestCatalog.StandardLayout("wrong", 99),
            request => AdaptiveCompositionTestCatalog.StandardLayout("still-wrong", 99));
        var composer = new AdaptiveSurfaceComposer(
            generator,
            new ComponentLayoutValidator(),
            new AdaptiveLayoutCache());
        using var session = Session();

        var result = await composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), session);

        Assert.Equal(AdaptiveCompositionSource.StandardLayout, result.Source);
        Assert.Contains("unexpected_layout_id", generator.Requests[1].CorrectionErrors, StringComparison.Ordinal);
        Assert.Contains("unexpected_revision", generator.Requests[1].CorrectionErrors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compose_ConcurrentRequest_SupersedesOlderGeneration()
    {
        var generator = new ConcurrentGenerator();
        var composer = new AdaptiveSurfaceComposer(
            generator,
            new ComponentLayoutValidator(),
            new AdaptiveLayoutCache());
        using var session = Session();

        var older = composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), session);
        await generator.FirstStarted.Task;
        var newer = await composer.ComposeAsync(AdaptiveCompositionTestCatalog.Context(), session);
        generator.ReleaseFirst.SetResult();

        Assert.Equal(2, newer.Generation);
        await Assert.ThrowsAsync<OperationCanceledException>(() => older);
    }

    private static AdaptiveSurfaceSession Session()
        => new(
            "product:watering-can",
            AdaptiveCompositionTestCatalog.Surface,
            AdaptiveCompositionTestCatalog.StandardLayout());

    private static ComponentLayoutDocument Invalid(AdaptiveSurfaceCompositionRequest request)
    {
        var standard = AdaptiveCompositionTestCatalog.StandardLayout(
            request.ExpectedLayoutId,
            request.ExpectedRevision);
        return standard with
        {
            Nodes =
            [
                standard.Nodes[0],
                standard.Nodes[1] with { Component = "InventedComponent" },
            ],
        };
    }

    private sealed class ScriptedGenerator(
        params Func<AdaptiveSurfaceCompositionRequest, ComponentLayoutDocument?>[] responses)
        : IAdaptiveLayoutGenerator
    {
        private readonly Queue<Func<AdaptiveSurfaceCompositionRequest, ComponentLayoutDocument?>> _responses = new(responses);

        public List<AdaptiveSurfaceCompositionRequest> Requests { get; } = [];

        public Task<AdaptiveLayoutGenerationResult> GenerateAsync(
            AdaptiveSurfaceCompositionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(new AdaptiveLayoutGenerationResult(
                _responses.Dequeue()(request),
                TimeSpan.Zero,
                InputTokens: null,
                OutputTokens: null));
        }
    }

    private sealed class CancelledGenerator : IAdaptiveLayoutGenerator
    {
        public Task<AdaptiveLayoutGenerationResult> GenerateAsync(
            AdaptiveSurfaceCompositionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromCanceled<AdaptiveLayoutGenerationResult>(cancellationToken);
    }

    private sealed class ConcurrentGenerator : IAdaptiveLayoutGenerator
    {
        private int _calls;

        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AdaptiveLayoutGenerationResult> GenerateAsync(
            AdaptiveSurfaceCompositionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstStarted.SetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }

            return new(
                AdaptiveCompositionTestCatalog.StandardLayout(
                    request.ExpectedLayoutId,
                    request.ExpectedRevision),
                TimeSpan.Zero,
                InputTokens: null,
                OutputTokens: null);
        }
    }
}
