using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Recipes;
using Xunit;
using BeanModel = CometBaristaNotes.Models.Bean;

namespace Comet.Tests.BaristaNotes.Services;

public sealed class RecipeSourcingTests
{
    [Fact]
    public void DefaultRegistry_RegistersGoldRoastersInOrder()
    {
        var registry = RoasterRecipeAdapterRegistry.CreateDefault(new HttpClient());

        Assert.Equal(
            ["onyx", "counterculture", "bluebottle", "intelligentsia"],
            registry.All.Select(adapter => adapter.Id));
        Assert.Equal(
            "counterculture",
            registry.FindAdapter(new BeanModel { Roaster = "Counter Culture Coffee" })?.Id);
    }

    [Fact]
    public void OnyxAdapter_ParsesPublishedGuideAndHeadingFormats()
    {
        var publishedGuide = """
            <div class="guide-body" data-guide="filter">
              <div id="wistia_id_brew_guide_filter_en"></div>
              <p>Coffee: 25g</p>
              <p>Water: 400g @ 205°F</p>
              <p><strong>Grind</strong><br>725µm</p>
            </div>
            """;
        var headingGuide = """
            <h2>Espresso Recipe</h2>
            <p>18g : 36g yield in 28 seconds</p>
            <p>Grind: medium-fine</p>
            <p>Temperature: 200°F</p>
            <h2>Shipping</h2>
            """;

        var filter = Assert.Single(OnyxCoffeeLabAdapter.ParseRecipes(publishedGuide, "https://source"));
        Assert.Equal(BrewMethod.PourOver, filter.BrewMethod);
        Assert.Equal(25m, filter.DoseIn);
        Assert.Equal(400m, filter.OutputAmount);
        Assert.Equal("725µm", filter.GrindHint);

        var espresso = Assert.Single(OnyxCoffeeLabAdapter.ParseRecipes(headingGuide, "https://source"));
        Assert.Equal(BrewMethod.Espresso, espresso.BrewMethod);
        Assert.Equal(18m, espresso.DoseIn);
        Assert.Equal(36m, espresso.OutputAmount);
        Assert.Equal(28m, espresso.TotalTimeSeconds);
    }

    [Fact]
    public async Task SourceRecipesAsync_UsesAdapterAndPersistsViaUpsert()
    {
        using var store = StoreWithBean();
        var recipeService = new TrackingRecipeService(new InMemoryRecipeService(store));
        var adapter = new FakeRecipeAdapter(
        [
            new ScrapedRecipe
            {
                BrewMethod = BrewMethod.Espresso,
                DoseIn = 18m,
                OutputAmount = 36m
            }
        ]);
        var service = new RecipeSourcingService(
            store,
            recipeService,
            new RoasterRecipeAdapterRegistry([adapter]),
            new FakeAIRecipeGenerator(isAvailable: true, []));

        var result = await service.SourceRecipesAsync(1);

        var recipe = Assert.Single(result);
        Assert.Equal(1, recipeService.UpsertCalls);
        Assert.Equal(RecipeSource.RoasterSite, recipe.Source);
        Assert.Single(store.Recipes);
    }

    [Fact]
    public async Task SourceRecipesAsync_FallsBackToAIAndPreservesUserEdits()
    {
        using var store = StoreWithBean();
        var recipes = new InMemoryRecipeService(store);
        var ai = new FakeAIRecipeGenerator(
            isAvailable: true,
            [new ScrapedRecipe { BrewMethod = BrewMethod.V60, DoseIn = 20m }]);
        var service = new RecipeSourcingService(
            store,
            recipes,
            new RoasterRecipeAdapterRegistry([new FakeRecipeAdapter([])]),
            ai);

        var first = Assert.Single(await service.SourceRecipesAsync(1));
        await recipes.UpdateAsync(first.Id, new UpdateRecipeDto { DoseIn = 21m });
        ai.Recipes =
        [
            new ScrapedRecipe
            {
                BrewMethod = BrewMethod.V60,
                DoseIn = 30m
            }
        ];

        var refreshed = Assert.Single(await service.SourceRecipesAsync(1));

        Assert.Equal(RecipeSource.AIGenerated, refreshed.Source);
        Assert.Equal(21m, refreshed.DoseIn);
        Assert.True(refreshed.IsEditedByUser);
    }

    [Fact]
    public async Task SourceRecipesAsync_CancellationIsBestEffortAndDoesNotPersist()
    {
        using var store = StoreWithBean();
        var adapter = new FakeRecipeAdapter([]);
        adapter.Fetch = (_, cancellationToken) =>
            Task.FromCanceled<IReadOnlyList<ScrapedRecipe>>(cancellationToken);
        var service = new RecipeSourcingService(
            store,
            new InMemoryRecipeService(store),
            new RoasterRecipeAdapterRegistry([adapter]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.SourceRecipesAsync(1, cancellation.Token);

        Assert.Empty(result);
        Assert.Equal(RecipeSourcingStatus.Cancelled, result.Status);
        Assert.Equal(RecipeSourcingErrors.Cancelled, result.ErrorCode);
        Assert.Empty(store.Recipes);
    }

    [Fact]
    public async Task SourceRecipesAsync_DistinguishesNoMatchUnavailableAndFailed()
    {
        using var store = StoreWithBean();
        var logger = new RecordingRecipeLogger();
        var emptyAdapter = new FakeRecipeAdapter([]);
        var noMatchService = new RecipeSourcingService(
            store,
            new InMemoryRecipeService(store),
            new RoasterRecipeAdapterRegistry([emptyAdapter]),
            logger: logger);
        var unavailableAdapter = new FakeRecipeAdapter([]);
        unavailableAdapter.Fetch = (_, _) =>
            Task.FromException<IReadOnlyList<ScrapedRecipe>>(
                new HttpRequestException("offline"));
        var unavailableService = new RecipeSourcingService(
            store,
            new InMemoryRecipeService(store),
            new RoasterRecipeAdapterRegistry([unavailableAdapter]),
            logger: logger);
        var failedAdapter = new FakeRecipeAdapter([]);
        failedAdapter.Fetch = (_, _) =>
            Task.FromException<IReadOnlyList<ScrapedRecipe>>(
                new FormatException("bad markup"));
        var failedService = new RecipeSourcingService(
            store,
            new InMemoryRecipeService(store),
            new RoasterRecipeAdapterRegistry([failedAdapter]),
            logger: logger);
        var persistence = new TrackingRecipeService(new InMemoryRecipeService(store))
        {
            UpsertException = new InvalidOperationException("save failed")
        };
        var persistenceService = new RecipeSourcingService(
            store,
            persistence,
            new RoasterRecipeAdapterRegistry(
            [
                new FakeRecipeAdapter(
                [
                    new ScrapedRecipe { BrewMethod = BrewMethod.Espresso }
                ])
            ]),
            logger: logger);

        var noMatch = await noMatchService.SourceRecipesAsync(1);
        var unavailable = await unavailableService.SourceRecipesAsync(1);
        var failed = await failedService.SourceRecipesAsync(1);
        var persistenceFailed = await persistenceService.SourceRecipesAsync(1);

        Assert.Equal(RecipeSourcingStatus.NoMatch, noMatch.Status);
        Assert.Equal(RecipeSourcingStatus.Unavailable, unavailable.Status);
        Assert.Equal(RecipeSourcingErrors.Unavailable, unavailable.ErrorCode);
        Assert.Equal(RecipeSourcingStatus.Failed, failed.Status);
        Assert.Equal(RecipeSourcingErrors.Failed, failed.ErrorCode);
        Assert.Equal(RecipeSourcingStatus.Failed, persistenceFailed.Status);
        Assert.Contains(logger.Entries, entry =>
            entry.BeanId == 1 &&
            entry.Source == "onyx" &&
            entry.Status == RecipeSourcingStatus.Failed);
    }

    [Fact]
    public async Task UpsertFromSource_PrefersUserEditedDuplicateAndRestoresUniqueness()
    {
        using var store = StoreWithBean();
        var now = DateTime.UtcNow;
        store.ExecuteMutation(() =>
        {
            store.Recipes.Add(new Recipe
            {
                Id = store.NextRecipeId(),
                BeanId = 1,
                BrewMethod = BrewMethod.Espresso,
                DoseIn = 21m,
                IsEditedByUser = true,
                FetchedAt = now.AddDays(-1)
            });
            store.Recipes.Add(new Recipe
            {
                Id = store.NextRecipeId(),
                BeanId = 1,
                BrewMethod = BrewMethod.Espresso,
                DoseIn = 30m,
                FetchedAt = now
            });
        });
        var service = new InMemoryRecipeService(store);

        var result = await service.UpsertFromSourceAsync(new CreateRecipeDto
        {
            BeanId = 1,
            BrewMethod = BrewMethod.Espresso,
            DoseIn = 18m
        });

        Assert.True(result.IsEditedByUser);
        Assert.Equal(21m, result.DoseIn);
        Assert.Single(store.Recipes.Where(recipe => !recipe.IsDeleted));
    }

    [Fact]
    public async Task UpsertFromSource_ConcurrentCalls_CreateOneRecipePerBeanAndMethod()
    {
        using var store = StoreWithBean();
        var service = new InMemoryRecipeService(store);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            Task.Run(() => service.UpsertFromSourceAsync(new CreateRecipeDto
            {
                BeanId = 1,
                BrewMethod = BrewMethod.Espresso,
                DoseIn = 18m + index
            }))));

        Assert.Single(store.Recipes.Where(recipe =>
            recipe.BeanId == 1 &&
            recipe.BrewMethod == BrewMethod.Espresso &&
            !recipe.IsDeleted));
    }

    [Fact]
    public async Task CreateBeanAsync_AfterSuccessfulSaveTriggersRecipeSourcing()
    {
        using var store = new InMemoryDataStore();
        var sourcing = new RecordingSourcingService();
        var service = new InMemoryBeanService(
            store,
            new DataChangeNotifier(),
            new InMemoryRatingService(store),
            sourcing);

        var created = await service.CreateBeanAsync(new CreateBeanDto { Name = "Monarch" });
        var sourcedBeanId = await sourcing.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(created.Success);
        Assert.Equal(created.Data!.Id, sourcedBeanId);
    }

    [Fact]
    public async Task CreateBeanAsync_BackgroundSourcing_DoesNotBlockAndPreservesForegroundWrites()
    {
        using var store = new BlockingSnapshotStore();
        var adapter = new BlockingRecipeAdapter();
        var sourcing = new CompletingSourcingService(new RecipeSourcingService(
            store,
            new InMemoryRecipeService(store),
            new RoasterRecipeAdapterRegistry([adapter])));
        var notifier = new DataChangeNotifier();
        var beans = new InMemoryBeanService(
            store,
            notifier,
            new InMemoryRatingService(store),
            sourcing);
        var bags = new InMemoryBagService(store, notifier);

        var createTask = beans.CreateBeanAsync(new CreateBeanDto
        {
            Name = "Monarch",
            Roaster = "Onyx Coffee Lab"
        });
        await adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(createTask.IsCompleted);
        var created = await createTask;
        Assert.True(created.Success);

        store.BlockNextSave();
        adapter.Release();
        await store.SnapshotCaptured.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var foregroundStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bagStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var foregroundTask = Task.Run(async () =>
        {
            foregroundStarted.SetResult();
            return await beans.UpdateBeanAsync(
                created.Data!.Id,
                new UpdateBeanDto { Name = "Foreground edit" });
        });
        var bagTask = Task.Run(async () =>
        {
            bagStarted.SetResult();
            return await bags.CreateNewBagForBeanAsync(
                created.Data!.Id,
                DateTime.Today);
        });
        await Task.WhenAll(
            foregroundStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            bagStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<TimeoutException>(
            () => Task.WhenAll(foregroundTask, bagTask)
                .WaitAsync(TimeSpan.FromMilliseconds(100)));

        store.ReleaseSave();
        await Task.WhenAll(sourcing.Completed.Task, foregroundTask, bagTask)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(bagTask.Result.Success);
        Assert.Equal("Foreground edit", store.PersistedBeanName);
        Assert.Equal(1, store.PersistedRecipeCount);
        Assert.Equal(1, store.PersistedBagCount);
    }

    [Fact]
    public async Task CreateBeanAsync_SourcingFailureIsLoggedWithBeanAndSourceContext()
    {
        using var store = new InMemoryDataStore();
        var logger = new RecordingRecipeLogger();
        var service = new InMemoryBeanService(
            store,
            new DataChangeNotifier(),
            new InMemoryRatingService(store),
            new ThrowingSourcingService(),
            logger);

        var created = await service.CreateBeanAsync(new CreateBeanDto { Name = "Monarch" });
        await logger.Logged.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(created.Success);
        Assert.Contains(logger.Entries, entry =>
            entry.BeanId == created.Data!.Id &&
            entry.Source == "post-bean-create" &&
            entry.Status == RecipeSourcingStatus.Failed);
    }

    [Fact]
    public async Task RecipePersistence_OverlappingForegroundMutation_PreservesBothSnapshots()
    {
        using var store = new BlockingSnapshotStore();
        store.Beans.Add(new BeanModel
        {
            Id = store.NextBeanId(),
            Name = "Original",
            Roaster = "Onyx Coffee Lab",
            IsActive = true
        });
        store.SaveChanges();

        var recipeService = new InMemoryRecipeService(store);
        var sourcing = new RecipeSourcingService(
            store,
            recipeService,
            new RoasterRecipeAdapterRegistry(
            [
                new FakeRecipeAdapter(
                [
                    new ScrapedRecipe
                    {
                        BrewMethod = BrewMethod.Espresso,
                        DoseIn = 18m,
                        OutputAmount = 36m
                    }
                ])
            ]));
        var beans = new InMemoryBeanService(
            store,
            new DataChangeNotifier(),
            new InMemoryRatingService(store));

        store.BlockNextSave();
        var recipeTask = Task.Run(() => sourcing.SourceRecipesAsync(1));
        await store.SnapshotCaptured.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var foregroundStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var foregroundTask = Task.Run(async () =>
        {
            foregroundStarted.SetResult();
            return await beans.UpdateBeanAsync(
                1,
                new UpdateBeanDto { Name = "Foreground edit" });
        });
        await foregroundStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<TimeoutException>(
            () => foregroundTask.WaitAsync(TimeSpan.FromMilliseconds(100)));

        store.ReleaseSave();
        await Task.WhenAll(recipeTask, foregroundTask);

        Assert.Equal("Foreground edit", store.PersistedBeanName);
        Assert.Equal(1, store.PersistedRecipeCount);
    }

    private static InMemoryDataStore StoreWithBean()
    {
        var store = new InMemoryDataStore();
        store.Beans.Add(new BeanModel
        {
            Id = store.NextBeanId(),
            Name = "Monarch",
            Roaster = "Onyx Coffee Lab",
            IsActive = true
        });
        return store;
    }

    private sealed class FakeRecipeAdapter(IReadOnlyList<ScrapedRecipe> recipes)
        : IRoasterRecipeAdapter
    {
        public string Id => "onyx";
        public string RoasterName => "Onyx Coffee Lab";
        public Func<BeanModel, CancellationToken, Task<IReadOnlyList<ScrapedRecipe>>>? Fetch { get; set; }

        public bool CanHandle(BeanModel bean) => true;

        public Task<IReadOnlyList<ScrapedRecipe>> FetchAsync(
            BeanModel bean,
            CancellationToken cancellationToken) =>
            Fetch?.Invoke(bean, cancellationToken) ?? Task.FromResult(recipes);
    }

    private sealed class FakeAIRecipeGenerator(
        bool isAvailable,
        IReadOnlyList<ScrapedRecipe> recipes) : IAIRecipeGenerator
    {
        public bool IsAvailable => isAvailable;
        public IReadOnlyList<ScrapedRecipe> Recipes { get; set; } = recipes;

        public Task<IReadOnlyList<ScrapedRecipe>> GenerateAsync(
            BeanModel bean,
            CancellationToken cancellationToken) =>
            Task.FromResult(Recipes);
    }

    private sealed class TrackingRecipeService(IRecipeService inner) : IRecipeService
    {
        public int UpsertCalls { get; private set; }
        public Exception? UpsertException { get; init; }

        public Task<IReadOnlyList<RecipeDto>> GetRecipesForBeanAsync(int beanId) =>
            inner.GetRecipesForBeanAsync(beanId);

        public Task<RecipeDto?> GetRecipeForBeanAndMethodAsync(int beanId, BrewMethod method) =>
            inner.GetRecipeForBeanAndMethodAsync(beanId, method);

        public Task<RecipeDto?> GetByIdAsync(int id) => inner.GetByIdAsync(id);
        public Task<RecipeDto> CreateAsync(CreateRecipeDto dto) => inner.CreateAsync(dto);

        public Task<RecipeDto> UpsertFromSourceAsync(CreateRecipeDto dto)
        {
            UpsertCalls++;
            return UpsertException is null
                ? inner.UpsertFromSourceAsync(dto)
                : Task.FromException<RecipeDto>(UpsertException);
        }

        public Task<RecipeDto> UpdateAsync(int id, UpdateRecipeDto dto) =>
            inner.UpdateAsync(id, dto);

        public Task DeleteAsync(int id) => inner.DeleteAsync(id);
    }

    private sealed class RecordingSourcingService : IRecipeSourcingService
    {
        public TaskCompletionSource<int> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RecipeSourcingResult> SourceRecipesAsync(
            int beanId,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(beanId);
            return Task.FromResult(new RecipeSourcingResult
            {
                Status = RecipeSourcingStatus.NoMatch
            });
        }
    }

    private sealed class CompletingSourcingService(IRecipeSourcingService inner)
        : IRecipeSourcingService
    {
        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RecipeSourcingResult> SourceRecipesAsync(
            int beanId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.SourceRecipesAsync(beanId, cancellationToken);
            }
            finally
            {
                Completed.TrySetResult();
            }
        }
    }

    private sealed class BlockingRecipeAdapter : IRoasterRecipeAdapter
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "onyx";
        public string RoasterName => "Onyx Coffee Lab";
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanHandle(BeanModel bean) => true;

        public async Task<IReadOnlyList<ScrapedRecipe>> FetchAsync(
            BeanModel bean,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return
            [
                new ScrapedRecipe
                {
                    BrewMethod = BrewMethod.Espresso,
                    DoseIn = 18m,
                    OutputAmount = 36m
                }
            ];
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class ThrowingSourcingService : IRecipeSourcingService
    {
        public Task<RecipeSourcingResult> SourceRecipesAsync(
            int beanId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<RecipeSourcingResult>(
                new InvalidOperationException("source failed"));
    }

    private sealed class RecordingRecipeLogger : IRecipeSourcingLogger
    {
        public List<(int BeanId, string Source, RecipeSourcingStatus Status)> Entries { get; } = [];
        public TaskCompletionSource Logged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void LogFailure(
            int beanId,
            string source,
            RecipeSourcingStatus status,
            string message,
            Exception? exception = null)
        {
            Entries.Add((beanId, source, status));
            Logged.TrySetResult();
        }
    }

    private sealed class BlockingSnapshotStore : InMemoryDataStore
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private int _blockNextSave;

        public TaskCompletionSource SnapshotCaptured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? PersistedBeanName { get; private set; }
        public int PersistedRecipeCount { get; private set; }
        public int PersistedBagCount { get; private set; }

        public void BlockNextSave() => Interlocked.Exchange(ref _blockNextSave, 1);
        public void ReleaseSave() => _release.Set();

        public override void SaveChanges()
        {
            var beanName = Beans.Single().Name;
            var recipeCount = Recipes.Count;
            var bagCount = Bags.Count;
            if (Interlocked.Exchange(ref _blockNextSave, 0) == 1)
            {
                SnapshotCaptured.TrySetResult();
                if (!_release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Test save was not released.");
            }

            PersistedBeanName = beanName;
            PersistedRecipeCount = recipeCount;
            PersistedBagCount = bagCount;
        }

        public override void Dispose()
        {
            _release.Dispose();
            base.Dispose();
        }
    }
}

public sealed class AIAdviceServiceTests
{
    [Fact]
    public async Task UnconfiguredService_ReturnsExplicitFailuresInsteadOfPlaceholders()
    {
        using var store = SeededStore();
        var service = new AIAdviceService(ShotService(store));

        Assert.False(await service.IsConfiguredAsync());

        var advice = await service.GetAdviceForShotAsync(store.Shots[0].Id);
        var recommendation = await service.GetRecommendationsForBeanAsync(store.Beans[0].Id);

        Assert.False(advice.Success);
        Assert.Equal(AIAdviceErrors.Unavailable, advice.ErrorCode);
        Assert.Empty(advice.Adjustments);
        Assert.False(recommendation.Success);
        Assert.Equal(AIAdviceErrors.Unavailable, recommendation.ErrorCode);
        Assert.Equal(0m, recommendation.Dose);
    }

    [Fact]
    public async Task ConfiguredService_UsesShotContextAndReturnsStructuredAdvice()
    {
        using var store = SeededStore();
        var adapter = new FakeAIAdviceAdapter
        {
            Advice = new AIAdviceResponseDto
            {
                Success = true,
                Adjustments =
                [
                    new ShotAdjustment
                    {
                        Parameter = "grind",
                        Direction = "finer",
                        Amount = "2 clicks"
                    }
                ],
                Reasoning = "The shot ran fast.",
                Source = "test"
            }
        };
        var service = new AIAdviceService(ShotService(store), adapter);

        var response = await service.GetAdviceForShotAsync(store.Shots[0].Id);

        Assert.True(response.Success);
        Assert.Single(response.Adjustments);
        Assert.Equal("finer grind 2 clicks", response.Adjustments[0].Recommendation);
        Assert.NotNull(adapter.LastAdviceRequest);
    }

    [Fact]
    public async Task ConfiguredService_RejectsSuccessShapedEmptyAdvice()
    {
        using var store = SeededStore();
        var adapter = new FakeAIAdviceAdapter
        {
            Advice = new AIAdviceResponseDto { Success = true }
        };
        var service = new AIAdviceService(ShotService(store), adapter);

        var response = await service.GetAdviceForShotAsync(store.Shots[0].Id);

        Assert.False(response.Success);
        Assert.Equal(AIAdviceErrors.InvalidResponse, response.ErrorCode);
    }

    [Fact]
    public async Task AdviceCancellation_ReturnsCancelledFailure()
    {
        using var store = SeededStore();
        var service = new AIAdviceService(ShotService(store), new FakeAIAdviceAdapter());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var response = await service.GetAdviceForShotAsync(
            store.Shots[0].Id,
            cancellation.Token);

        Assert.False(response.Success);
        Assert.Equal(AIAdviceErrors.Cancelled, response.ErrorCode);
    }

    [Fact]
    public async Task RecommendationCancellation_PropagatesToCaller()
    {
        using var store = SeededStore();
        var service = new AIAdviceService(ShotService(store), new FakeAIAdviceAdapter());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetRecommendationsForBeanAsync(
                store.Beans[0].Id,
                cancellation.Token));
    }

    [Fact]
    public async Task AdapterErrors_MapToStableServiceStates()
    {
        using var store = SeededStore();
        var adapter = new FakeAIAdviceAdapter
        {
            AdviceException = new HttpRequestException("offline")
        };
        var service = new AIAdviceService(ShotService(store), adapter);

        var offline = await service.GetAdviceForShotAsync(store.Shots[0].Id);
        adapter.AdviceException = new InvalidOperationException("429 rate exceeded");
        var rateLimited = await service.GetAdviceForShotAsync(store.Shots[0].Id);

        Assert.Equal(AIAdviceErrors.Connectivity, offline.ErrorCode);
        Assert.Equal(AIAdviceErrors.RateLimited, rateLimited.ErrorCode);
    }

    [Fact]
    public async Task FallbackAdapter_DisablesFailedLocalAdapterForSession()
    {
        using var store = SeededStore();
        var local = new FakeAIAdviceAdapter
        {
            AdviceException = new InvalidOperationException("local unavailable")
        };
        var cloud = new FakeAIAdviceAdapter
        {
            Advice = new AIAdviceResponseDto
            {
                Success = true,
                Adjustments =
                [
                    new ShotAdjustment
                    {
                        Parameter = "yield",
                        Direction = "increase",
                        Amount = "2g"
                    }
                ],
                Source = "via cloud"
            }
        };
        var service = new AIAdviceService(
            ShotService(store),
            new FallbackAIAdviceAdapter(local, cloud));

        var first = await service.GetAdviceForShotAsync(store.Shots[0].Id);
        var second = await service.GetAdviceForShotAsync(store.Shots[0].Id);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal("via cloud", first.Source);
        Assert.Equal(1, local.AdviceCalls);
        Assert.Equal(2, cloud.AdviceCalls);
    }

    [Fact]
    public async Task AzureOpenAIAdapter_ConfiguredProviderExecutesAndParsesAdvice()
    {
        using var store = SeededStore();
        var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler);
        var adapter = new AzureOpenAIAdviceAdapter(
            httpClient,
            new AzureOpenAIAdviceConfiguration
            {
                Endpoint = "https://example.openai.azure.com",
                ApiKey = "test-api-key",
                DeploymentName = "test-deployment"
            });
        var service = new AIAdviceService(ShotService(store), adapter);

        var response = await service.GetAdviceForShotAsync(store.Shots[0].Id);

        Assert.True(await service.IsConfiguredAsync());
        Assert.True(response.Success);
        Assert.Equal("via Azure OpenAI", response.Source);
        Assert.Equal("finer", Assert.Single(response.Adjustments).Direction);
        Assert.Contains(
            "/openai/deployments/test-deployment/chat/completions",
            handler.RequestUri!.AbsolutePath);
        Assert.Equal("test-api-key", handler.ApiKey);
        Assert.Contains("\"response_format\"", handler.RequestBody);
        Assert.Contains("## Current Shot", handler.RequestBody);
        Assert.Contains("do NOT apply assumptions from other brewing methods", handler.RequestBody);
        Assert.DoesNotContain("ShotId", handler.RequestBody);
        Assert.DoesNotContain("AvatarPath", handler.RequestBody);
        Assert.DoesNotContain("CreatedAt", handler.RequestBody);
    }

    private static InMemoryDataStore SeededStore()
    {
        var store = new InMemoryDataStore();
        store.Seed();
        return store;
    }

    private static InMemoryShotService ShotService(InMemoryDataStore store) =>
        new(store, new DataChangeNotifier());

    private sealed class FakeAIAdviceAdapter : IAIAdviceAdapter
    {
        public bool IsConfigured { get; set; } = true;
        public int AdviceCalls { get; private set; }
        public AIAdviceRequestDto? LastAdviceRequest { get; private set; }
        public Exception? AdviceException { get; set; }
        public AIAdviceResponseDto Advice { get; set; } = new()
        {
            Success = true,
            Adjustments =
            [
                new ShotAdjustment
                {
                    Parameter = "grind",
                    Direction = "finer",
                    Amount = "1 click"
                }
            ]
        };

        public Task<AIAdviceResponseDto> GetShotAdviceAsync(
            AIAdviceRequestDto request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdviceCalls++;
            LastAdviceRequest = request;
            return AdviceException is null
                ? Task.FromResult(Advice)
                : Task.FromException<AIAdviceResponseDto>(AdviceException);
        }

        public Task<string?> GetPassiveInsightAsync(
            AIAdviceRequestDto request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("Try a finer grind.");

        public Task<AIRecommendationDto> GetBeanRecommendationAsync(
            BeanRecommendationContextDto context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AIRecommendationDto
            {
                Success = true,
                Dose = 18m,
                GrindSetting = "medium-fine",
                Output = 36m,
                Duration = 28m,
                Source = "test"
            });
        }
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("api-key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var advice = JsonSerializer.Serialize(new
            {
                adjustments = new[]
                {
                    new
                    {
                        parameter = "grind",
                        direction = "finer",
                        amount = "1 click"
                    }
                },
                reasoning = "The shot ran quickly."
            });
            var envelope = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = advice } }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json")
            };
        }
    }
}
