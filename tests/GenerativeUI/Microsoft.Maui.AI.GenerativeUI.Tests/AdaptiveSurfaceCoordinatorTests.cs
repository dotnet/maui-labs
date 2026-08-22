using System.Text.Json;
using AIExtensions.Sample.Garden.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class AdaptiveSurfaceCoordinatorTests
{
    [Fact]
    public async Task Activate_RendersStandardImmediatelyThenAppliesGeneratedLayout()
    {
        var generator = new BlockingGenerator();
        using var harness = CreateHarness(generator, "product:first");

        await harness.Coordinator.ActivateAsync(harness.Surface);

        Assert.Same(harness.Surface.Session.StandardLayout, harness.Surface.Session.CurrentLayout);
        Assert.False(harness.Surface.Session.IsSuspended);
        generator.Release.SetResult();
        await harness.Coordinator.WhenIdleAsync();

        Assert.Equal(AdaptiveCompositionSource.Generated, harness.GeneratorResultSource());
        Assert.Equal("compact", harness.Surface.Session.CurrentLayout!.Nodes[1].Variant);
        Assert.False(harness.Surface.Session.IsStandardLayout);
    }

    [Fact]
    public async Task PublishIntent_DebouncesAndNormalizesLatestUserIntent()
    {
        var generator = new RecordingGenerator();
        using var harness = CreateHarness(generator, "product:first");
        await harness.Coordinator.ActivateAsync(harness.Surface);
        await harness.Coordinator.WhenIdleAsync();

        harness.Coordinator.PublishIntent("  balcony   herb garden ");
        harness.Coordinator.PublishIntent(" compact   cart ");
        Assert.True(harness.Coordinator.CurrentStatus!.IsComposing);
        await harness.Coordinator.WhenIdleAsync();

        Assert.Equal(2, generator.Requests.Count);
        Assert.Equal("compact cart", generator.Requests[^1].Context.Intent);
        Assert.Equal(
            ["balcony herb garden", "compact cart"],
            generator.Requests[^1].Context.RecentContext);
    }

    [Fact]
    public async Task Activate_NewSurfaceCancelsStaleResultAndSuspendsStackedPage()
    {
        var generator = new FirstCallBlockingGenerator();
        using var harness = CreateHarness(generator, "product:first");
        var second = harness.CreateSurface("product:second");
        await harness.Coordinator.ActivateAsync(harness.Surface);
        await generator.FirstStarted.Task;

        await harness.Coordinator.ActivateAsync(second);
        await harness.Coordinator.WhenIdleAsync();

        Assert.True(harness.Surface.Session.IsSuspended);
        Assert.True(harness.Surface.Session.IsStandardLayout);
        Assert.False(second.Session.IsSuspended);
        Assert.False(second.Session.IsStandardLayout);
        Assert.Equal("product:second", harness.Coordinator.CurrentStatus!.SurfaceInstanceId);
    }

    [Fact]
    public async Task Reset_RestoresStandardAndClearsPresentationIntent()
    {
        var generator = new RecordingGenerator();
        using var harness = CreateHarness(generator, "product:first");
        await harness.Coordinator.ActivateAsync(harness.Surface);
        await harness.Coordinator.WhenIdleAsync();
        harness.Coordinator.PublishIntent("show dimensions");
        await harness.Coordinator.WhenIdleAsync();

        await harness.Coordinator.ResetToStandardAsync();

        Assert.True(harness.Surface.Session.IsStandardLayout);
        Assert.Null(harness.Coordinator.LatestIntent);
        Assert.False(harness.Coordinator.CurrentStatus!.IsAdapted);
    }

    [Fact]
    public async Task GenerationFailure_AfterAdaptation_PreservesAdaptedStatus()
    {
        using var harness = CreateHarness(new SuccessThenFailureGenerator(), "product:first");
        await harness.Coordinator.ActivateAsync(harness.Surface);
        await harness.Coordinator.WhenIdleAsync();
        Assert.True(harness.Coordinator.CurrentStatus!.IsAdapted);

        harness.Coordinator.PublishIntent("keep the current useful layout");
        await harness.Coordinator.WhenIdleAsync();

        Assert.False(harness.Surface.Session.IsStandardLayout);
        Assert.True(harness.Coordinator.CurrentStatus!.IsAdapted);
        Assert.Equal(
            "Compact for the current intent.",
            harness.Coordinator.CurrentStatus.Explanation);
    }

    [Fact]
    public async Task Reactivate_AfterReset_ReusesCachedLayout()
    {
        var generator = new RecordingGenerator();
        using var harness = CreateHarness(generator, "product:first");
        await harness.Coordinator.ActivateAsync(harness.Surface);
        await harness.Coordinator.WhenIdleAsync();
        await harness.Coordinator.ResetToStandardAsync();

        await harness.Coordinator.ActivateAsync(harness.Surface);
        await harness.Coordinator.WhenIdleAsync();

        Assert.Single(generator.Requests);
        Assert.False(harness.Surface.Session.IsStandardLayout);
    }

    private static CoordinatorHarness CreateHarness(
        IAdaptiveLayoutGenerator generator,
        string surfaceInstanceId)
    {
        var registry = new GenerativeUiRegistry()
            .AddComponent<AdaptiveRegionRendererTests.GridComponent>(new ComponentDescriptor
            {
                Alias = "ProductHero",
                Description = "Product identity.",
                DataContract = nameof(Product),
                RequiredBindings = ["name"],
                Variants = ["default", "compact"],
            });
        var services = new ServiceCollection().BuildServiceProvider();
        var renderer = new RecordingRenderer(
            new AdaptiveRegionRenderer(registry, services));
        var coordinator = new AdaptiveSurfaceCoordinator(
            new AdaptiveSurfaceComposer(
                generator,
                new ComponentLayoutValidator(),
                new AdaptiveLayoutCache()),
            renderer.Inner,
            new ImmediateDispatcher(),
            new NoOpTransition());
        var harness = new CoordinatorHarness(
            services,
            coordinator,
            renderer,
            surfaceInstanceId);
        return harness;
    }

    private sealed class TestSurface : IAdaptiveSurface
    {
        public TestSurface(string instanceId)
        {
            Session = new(
                instanceId,
                AdaptiveCompositionTestCatalog.Surface,
                AdaptiveCompositionTestCatalog.StandardLayout());
            UiObjectBuilder.Replace(
                Session.StateRoot["product"],
                JsonSerializer.SerializeToElement(
                    GardenProductFixtures.WateringCan,
                    GardenJsonContext.Default.Product));
            new AdaptiveRegionView(AdaptiveCompositionTestCatalog.Region).Attach(Session);
        }

        public AdaptiveSurfaceSession Session { get; }

        public ValueTask<AdaptiveSurfaceContext> CreateContextAsync(
            PresentationIntentContext presentation,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                AdaptiveCompositionTestCatalog.Context() with
                {
                    SurfaceInstanceId = Session.SurfaceInstanceId,
                    Intent = presentation.Intent,
                    RecentContext = presentation.RecentUserContext,
                });
    }

    private sealed class ImmediateDispatcher : IAdaptiveSurfaceDispatcher
    {
        public Task DispatchAsync(Func<Task> action) => action();

        public Task<T> DispatchAsync<T>(Func<Task<T>> action) => action();
    }

    private sealed class NoOpTransition : IAdaptiveSurfaceTransition
    {
        public Task AnimateAsync(
            AdaptiveSurfaceSession session,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class BlockingGenerator : IAdaptiveLayoutGenerator
    {
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AdaptiveLayoutGenerationResult> GenerateAsync(
            AdaptiveSurfaceCompositionRequest request,
            CancellationToken cancellationToken = default)
        {
            await Release.Task.WaitAsync(cancellationToken);
            return Result(request);
        }
    }

    private sealed class FirstCallBlockingGenerator : IAdaptiveLayoutGenerator
    {
        private int _calls;

        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AdaptiveLayoutGenerationResult> GenerateAsync(
            AdaptiveSurfaceCompositionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Result(request);
        }
    }

    private sealed class RecordingGenerator : IAdaptiveLayoutGenerator
    {
        public List<AdaptiveSurfaceCompositionRequest> Requests { get; } = [];

        public Task<AdaptiveLayoutGenerationResult> GenerateAsync(
            AdaptiveSurfaceCompositionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result(request));
        }
    }

    private sealed class SuccessThenFailureGenerator : IAdaptiveLayoutGenerator
    {
        private int _calls;

        public Task<AdaptiveLayoutGenerationResult> GenerateAsync(
            AdaptiveSurfaceCompositionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                return Task.FromResult(Result(request));

            throw new InvalidOperationException("Simulated model outage.");
        }
    }

    private static AdaptiveLayoutGenerationResult Result(AdaptiveSurfaceCompositionRequest request)
    {
        var layout = AdaptiveCompositionTestCatalog.StandardLayout(
            request.ExpectedLayoutId,
            request.ExpectedRevision);
        return new(
            layout with
            {
                Explanation = "Compact for the current intent.",
                Nodes =
                [
                    layout.Nodes[0],
                    layout.Nodes[1] with { Variant = "compact" },
                ],
            },
            TimeSpan.Zero,
            InputTokens: null,
            OutputTokens: null);
    }

    private sealed class RecordingRenderer(AdaptiveRegionRenderer inner)
    {
        public AdaptiveRegionRenderer Inner { get; } = inner;

        public AdaptiveCompositionSource? LastSource { get; set; }
    }

    private sealed class CoordinatorHarness : IDisposable
    {
        public CoordinatorHarness(
            ServiceProvider services,
            AdaptiveSurfaceCoordinator coordinator,
            RecordingRenderer renderer,
            string surfaceInstanceId)
        {
            Services = services;
            Coordinator = coordinator;
            Renderer = renderer;
            Surface = CreateSurface(surfaceInstanceId);
        }

        public ServiceProvider Services { get; }

        public AdaptiveSurfaceCoordinator Coordinator { get; }

        public RecordingRenderer Renderer { get; }

        public TestSurface Surface { get; }

        public TestSurface CreateSurface(string instanceId) => new(instanceId);

        public AdaptiveCompositionSource GeneratorResultSource()
            => Surface.Session.IsStandardLayout
                ? AdaptiveCompositionSource.StandardLayout
                : AdaptiveCompositionSource.Generated;

        public void Dispose()
        {
            Surface.Session.Dispose();
            Coordinator.Dispose();
            Services.Dispose();
        }
    }
}
