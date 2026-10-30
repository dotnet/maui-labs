using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Grind;
using Xunit;
using IOPath = System.IO.Path;
using RealDataChangeType = CometBaristaNotes.Models.Enums.DataChangeType;
using RealEquipmentType = CometBaristaNotes.Models.Enums.EquipmentType;

namespace Comet.Tests.BaristaNotes.Persistence;

public sealed class BaristaNotesPersistenceTests : IDisposable
{
    private readonly string _directory = IOPath.Combine(
        Environment.CurrentDirectory,
        "artifacts",
        "baristanotes-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SqliteCreateAndReopen_PreservesDeterministicSeed()
    {
        var path = DatabasePath();
        using (var first = new SqliteDataStore(path))
        {
            Assert.Equal(SqliteDataStore.CurrentSchemaVersion, first.SchemaVersion);
            Assert.Equal(2, first.Beans.Count);
            Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000001"), first.Beans[0].SyncId);
        }

        using var reopened = new SqliteDataStore(path);
        Assert.Equal(2, reopened.Beans.Count);
        Assert.Single(reopened.Shots);
        Assert.Equal("Ethiopian Yirgacheffe", reopened.Shots[0].Bag!.Bean!.Name);
    }

    [Fact]
    public async Task CrudAndRelaunch_PreservesUpdatesArchivesAndDeletes()
    {
        var path = DatabasePath();
        int beanId;
        int equipmentId;
        using (var store = new SqliteDataStore(path, seedOnFirstRun: false))
        {
            var notifier = new DataChangeNotifier();
            var ratings = new InMemoryRatingService(store);
            var beans = new InMemoryBeanService(store, notifier, ratings);
            var equipment = new InMemoryEquipmentService(store, notifier);
            var profiles = new InMemoryUserProfileService(
                store,
                notifier,
                new LocalImageProcessingService(System.IO.Path.GetDirectoryName(path)));

            beanId = (await beans.CreateBeanAsync(new CreateBeanDto { Name = "Persisted bean" })).Data!.Id;
            equipmentId = (await equipment.CreateEquipmentAsync(
                new CreateEquipmentDto { Name = "Persisted grinder", Type = RealEquipmentType.Grinder })).Id;
            var profile = await profiles.CreateProfileAsync(new CreateUserProfileDto { Name = "Temporary profile" });
            await beans.ArchiveBeanAsync(beanId);
            await equipment.ArchiveEquipmentAsync(equipmentId);
            await profiles.DeleteProfileAsync(profile.Id);
        }

        using var reopened = new SqliteDataStore(path, seedOnFirstRun: false);
        Assert.False(reopened.Beans.Single(bean => bean.Id == beanId).IsActive);
        Assert.False(reopened.Equipment.Single(item => item.Id == equipmentId).IsActive);
        Assert.True(reopened.Profiles.Single().IsDeleted);
    }

    [Fact]
    public async Task ShotFilteringPagingAndOrdering_SurviveRelaunch()
    {
        var path = DatabasePath();
        int beanId;
        using (var store = new SqliteDataStore(path, seedOnFirstRun: false))
        {
            var notifier = new DataChangeNotifier();
            var ratings = new InMemoryRatingService(store);
            var beans = new InMemoryBeanService(store, notifier, ratings);
            var bags = new InMemoryBagService(store, notifier);
            var shots = new InMemoryShotService(store, notifier);
            beanId = (await beans.CreateBeanAsync(new CreateBeanDto { Name = "Filter bean" })).Data!.Id;
            var bag = (await bags.CreateNewBagForBeanAsync(beanId, DateTime.Today)).Data!;

            await shots.CreateShotAsync(Shot(bag.Id, new DateTime(2026, 1, 1), 2));
            await shots.CreateShotAsync(Shot(bag.Id, new DateTime(2026, 3, 1), 4));
            await shots.CreateShotAsync(Shot(bag.Id, new DateTime(2026, 2, 1), 4));
        }

        using var reopened = new SqliteDataStore(path, seedOnFirstRun: false);
        var service = new InMemoryShotService(reopened, new DataChangeNotifier());
        var page = await service.GetFilteredShotHistoryAsync(
            new ShotFilterCriteriaDto { BeanIds = new[] { beanId }, Ratings = new[] { 4 } },
            pageIndex: 0,
            pageSize: 1);
        Assert.Equal(2, page.TotalCount);
        Assert.True(page.HasNextPage);
        Assert.Equal(new DateTime(2026, 3, 1), page.Items[0].Timestamp);
    }

    [Fact]
    public async Task ShotUpdateDeleteAndBagCompletion_PersistAcrossRelaunch()
    {
        var path = DatabasePath();
        int shotId;
        int bagId;
        using (var store = new SqliteDataStore(path))
        {
            var notifier = new DataChangeNotifier();
            var preferences = new PreferencesService(store.CreatePreferencesStore());
            var shots = new InMemoryShotService(store, notifier, preferences);
            var bags = new InMemoryBagService(store, notifier);
            bagId = store.Bags[0].Id;
            var created = await shots.CreateShotAsync(Shot(bagId, new DateTime(2026, 4, 1), 3));
            shotId = created.Id;
            await shots.UpdateShotAsync(shotId, new UpdateShotDto
            {
                DrinkType = "Cortado",
                Rating = 4,
                ActualTime = 29m,
                ActualOutput = 38m
            });
            await bags.MarkBagCompleteAsync(bagId);
        }

        using (var reopened = new SqliteDataStore(path))
        {
            Assert.True(reopened.Bags.Single(item => item.Id == bagId).IsComplete);
            Assert.Equal(bagId, new PreferencesService(reopened.CreatePreferencesStore()).GetLastBagId());
            var shots = new InMemoryShotService(reopened, new DataChangeNotifier());
            var updated = await shots.GetShotByIdAsync(shotId);
            Assert.Equal("Cortado", updated!.DrinkType);
            Assert.Equal(4, updated.Rating);
            await shots.DeleteShotAsync(shotId);
        }

        using var relaunched = new SqliteDataStore(path);
        Assert.True(relaunched.Shots.Single(item => item.Id == shotId).IsDeleted);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ShotUpdate_ExplicitEquipmentClearsPersistAndOmittedIdsArePreserved(
        bool clearMachine,
        bool clearGrinder)
    {
        var path = DatabasePath();
        int shotId;
        int? expectedMachineId;
        int? expectedGrinderId;
        using (var store = new SqliteDataStore(path))
        {
            var original = Assert.Single(store.Shots);
            shotId = original.Id;
            Assert.NotNull(original.MachineId);
            Assert.NotNull(original.GrinderId);
            expectedMachineId = clearMachine ? null : original.MachineId;
            expectedGrinderId = clearGrinder ? null : original.GrinderId;
            var service = new InMemoryShotService(store, new DataChangeNotifier());

            var updated = await service.UpdateShotAsync(shotId, new UpdateShotDto
            {
                ClearMachine = clearMachine,
                ClearGrinder = clearGrinder,
                DrinkType = original.DrinkType,
                Rating = original.Rating,
                TastingNotes = original.TastingNotes
            });

            Assert.Equal(expectedMachineId, updated.Machine?.Id);
            Assert.Equal(expectedGrinderId, updated.Grinder?.Id);
            Assert.Equal(expectedMachineId, original.MachineId);
            Assert.Equal(expectedMachineId, original.Machine?.Id);
            Assert.Equal(expectedGrinderId, original.GrinderId);
            Assert.Equal(expectedGrinderId, original.Grinder?.Id);
        }

        using var reopened = new SqliteDataStore(path);
        var record = Assert.Single(reopened.Shots);
        var reloaded = await new InMemoryShotService(reopened, new DataChangeNotifier())
            .GetShotByIdAsync(shotId);
        Assert.NotNull(reloaded);
        Assert.Equal(expectedMachineId, record.MachineId);
        Assert.Equal(expectedMachineId, record.Machine?.Id);
        Assert.Equal(expectedGrinderId, record.GrinderId);
        Assert.Equal(expectedGrinderId, record.Grinder?.Id);
        Assert.Equal(expectedMachineId, reloaded.Machine?.Id);
        Assert.Equal(expectedGrinderId, reloaded.Grinder?.Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ShotUpdate_EquipmentReplacementPersistsWithoutClearFlags(
        bool replaceMachine,
        bool replaceGrinder)
    {
        var path = DatabasePath();
        int? expectedMachineId;
        int? expectedGrinderId;
        using (var store = new SqliteDataStore(path))
        {
            var notifier = new DataChangeNotifier();
            var equipment = new InMemoryEquipmentService(store, notifier);
            var machine = await equipment.CreateEquipmentAsync(
                new CreateEquipmentDto { Name = "Replacement machine", Type = RealEquipmentType.Machine });
            var grinder = await equipment.CreateEquipmentAsync(
                new CreateEquipmentDto { Name = "Replacement grinder", Type = RealEquipmentType.Grinder });
            var original = Assert.Single(store.Shots);
            expectedMachineId = replaceMachine ? machine.Id : original.MachineId;
            expectedGrinderId = replaceGrinder ? grinder.Id : original.GrinderId;

            var updated = await new InMemoryShotService(store, notifier)
                .UpdateShotAsync(original.Id, new UpdateShotDto
                {
                    MachineId = replaceMachine ? machine.Id : null,
                    GrinderId = replaceGrinder ? grinder.Id : null,
                    DrinkType = original.DrinkType,
                    Rating = original.Rating,
                    TastingNotes = original.TastingNotes
                });

            Assert.Equal(expectedMachineId, updated.Machine?.Id);
            Assert.Equal(expectedGrinderId, updated.Grinder?.Id);
        }

        using var reopened = new SqliteDataStore(path);
        var record = Assert.Single(reopened.Shots);
        Assert.Equal(expectedMachineId, record.MachineId);
        Assert.Equal(expectedMachineId, record.Machine?.Id);
        Assert.Equal(expectedGrinderId, record.GrinderId);
        Assert.Equal(expectedGrinderId, record.Grinder?.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShotUpdate_SetAndClearTheSameEquipmentRejectsWithoutChangingTheRecord(bool clearMachine)
    {
        var path = DatabasePath();
        int? machineId;
        int? grinderId;
        using (var store = new SqliteDataStore(path))
        {
            var original = Assert.Single(store.Shots);
            machineId = original.MachineId;
            grinderId = original.GrinderId;
            var service = new InMemoryShotService(store, new DataChangeNotifier());

            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShotAsync(original.Id, new UpdateShotDto
            {
                MachineId = machineId,
                GrinderId = grinderId,
                ClearMachine = clearMachine,
                ClearGrinder = !clearMachine,
                DrinkType = original.DrinkType,
                Rating = original.Rating
            }));

            Assert.Equal(machineId, original.MachineId);
            Assert.Equal(machineId, original.Machine?.Id);
            Assert.Equal(grinderId, original.GrinderId);
            Assert.Equal(grinderId, original.Grinder?.Id);
        }

        using var reopened = new SqliteDataStore(path);
        var record = Assert.Single(reopened.Shots);
        Assert.Equal(machineId, record.MachineId);
        Assert.Equal(grinderId, record.GrinderId);
    }

    [Fact]
    public async Task DeletedBagShots_RemainInBeanHistoryRatingsAndGrindHistoryAfterRelaunch()
    {
        var path = DatabasePath();
        int beanId;
        int bagId;
        int grinderId;
        using (var store = new SqliteDataStore(path, seedOnFirstRun: false))
        {
            var notifier = new DataChangeNotifier();
            var bean = (await new InMemoryBeanService(store, notifier, new InMemoryRatingService(store))
                .CreateBeanAsync(new CreateBeanDto { Name = "Historical bean" })).Data!;
            beanId = bean.Id;
            var bag = (await new InMemoryBagService(store, notifier)
                .CreateNewBagForBeanAsync(beanId, new DateTime(2026, 8, 1))).Data!;
            bagId = bag.Id;
            var grinder = await new InMemoryEquipmentService(store, notifier).CreateEquipmentAsync(
                new CreateEquipmentDto { Name = "Turin DF64V", Type = RealEquipmentType.Grinder });
            grinderId = grinder.Id;
            await new InMemoryShotService(store, notifier).CreateShotAsync(new CreateShotDto
            {
                BagId = bagId,
                GrinderId = grinderId,
                Timestamp = new DateTime(2026, 8, 2),
                BrewMethod = BrewMethod.Espresso,
                DoseIn = 18m,
                GrindMicrons = 600,
                ExpectedTime = 28m,
                ExpectedOutput = 36m,
                DrinkType = "Espresso",
                Rating = 4
            });
            await new InMemoryBagService(store, notifier).DeleteBagAsync(bagId);
        }

        using var reopened = new SqliteDataStore(path, seedOnFirstRun: false);
        Assert.True(reopened.Bags.Single(item => item.Id == bagId).IsDeleted);

        var history = await new InMemoryShotService(reopened, new DataChangeNotifier())
            .GetShotHistoryByBeanAsync(beanId, pageIndex: 0, pageSize: 10);
        Assert.Single(history.Items);
        Assert.Equal(1, history.TotalCount);

        var rating = await new InMemoryRatingService(reopened).GetBeanRatingAsync(beanId);
        Assert.Equal(1, rating.TotalShots);
        Assert.Equal(1, rating.RatedShots);
        Assert.Equal(4, rating.AverageRating);

        var grind = await new InMemoryGrindTranslationService(reopened).TranslateAsync(new(
            grinderId,
            "Turin DF64V",
            "600 microns",
            BrewMethod.Espresso,
            beanId));
        Assert.Equal(GrindTranslationSource.UserHistory, grind.Source);
        Assert.Equal(50m, grind.SuggestedSetting);
    }

    [Fact]
    public async Task DeletedBagShots_BestRatedLookupSurvivesRelaunch()
    {
        var fixture = await CreateDeletedBagHistoryAsync();

        using var reopened = new SqliteDataStore(DatabasePath(), seedOnFirstRun: false);
        var best = await new InMemoryShotService(reopened, new DataChangeNotifier())
            .GetBestRatedShotByBeanAsync(fixture.BeanId);

        Assert.NotNull(best);
        Assert.Equal(fixture.BestShotId, best.Id);
        Assert.Equal(4, best.Rating);
    }

    [Fact]
    public async Task DeletedBagShots_AIShotContextSurvivesRelaunch()
    {
        var fixture = await CreateDeletedBagHistoryAsync();

        using var reopened = new SqliteDataStore(DatabasePath(), seedOnFirstRun: false);
        var context = await new InMemoryShotService(reopened, new DataChangeNotifier())
            .GetShotContextForAIAsync(fixture.CurrentShotId);

        Assert.NotNull(context);
        var historical = Assert.Single(context.HistoricalShots);
        Assert.Equal(4, historical.Rating);
    }

    [Fact]
    public async Task DeletedBagShots_BeanHasHistorySurvivesRelaunch()
    {
        var fixture = await CreateDeletedBagHistoryAsync();

        using var reopened = new SqliteDataStore(DatabasePath(), seedOnFirstRun: false);
        Assert.True(await new InMemoryShotService(reopened, new DataChangeNotifier())
            .BeanHasHistoryAsync(fixture.BeanId));
    }

    [Fact]
    public async Task DeletedBagShots_BeanRecommendationHistorySurvivesRelaunch()
    {
        var fixture = await CreateDeletedBagHistoryAsync();

        using var reopened = new SqliteDataStore(DatabasePath(), seedOnFirstRun: false);
        var context = await new InMemoryShotService(reopened, new DataChangeNotifier())
            .GetBeanRecommendationContextAsync(fixture.BeanId);

        Assert.NotNull(context);
        Assert.True(context.HasHistory);
        Assert.Equal(2, context.HistoricalShots!.Count);
        Assert.Equal([4, 3], context.HistoricalShots.Select(shot => shot.Rating));
    }

    [Fact]
    public void PreferencesAndValueRanges_PersistAcrossReopen()
    {
        var path = DatabasePath();
        using (var store = new SqliteDataStore(path, seedOnFirstRun: false))
        {
            var preferences = new PreferencesService(store.CreatePreferencesStore());
            preferences.SetLastDrinkType("Cortado");
            preferences.SetLastAccessoryIds([7, 9]);
            var ranges = new InMemoryDrinkValueRangeService(preferences);
            ranges.SaveOverride(DrinkValueMetric.DoseIn, BrewMethod.Espresso, 16m, 22m);
        }

        using var reopened = new SqliteDataStore(path, seedOnFirstRun: false);
        var reopenedPreferences = new PreferencesService(reopened.CreatePreferencesStore());
        var reopenedRanges = new InMemoryDrinkValueRangeService(reopenedPreferences);
        Assert.Equal("Cortado", reopenedPreferences.GetLastDrinkType());
        Assert.Equal([7, 9], reopenedPreferences.GetLastAccessoryIds());
        Assert.Equal(ValueRangeSource.Custom, reopenedRanges.Resolve(DrinkValueMetric.DoseIn, BrewMethod.Espresso).Source);
    }

    [Fact]
    public async Task RecipeUpsert_PreservesUserEditsAndBeanDeletePreservesDependents()
    {
        var path = DatabasePath();
        using var store = new SqliteDataStore(path, seedOnFirstRun: false);
        var notifier = new DataChangeNotifier();
        var beanService = new InMemoryBeanService(store, notifier, new InMemoryRatingService(store));
        var recipeService = new InMemoryRecipeService(store);
        var bean = (await beanService.CreateBeanAsync(new CreateBeanDto { Name = "Recipe bean" })).Data!;
        var recipe = await recipeService.CreateAsync(new CreateRecipeDto
        {
            BeanId = bean.Id,
            BrewMethod = BrewMethod.V60,
            Source = RecipeSource.RoasterSite,
            Title = "Original"
        });
        await recipeService.UpdateAsync(recipe.Id, new UpdateRecipeDto { Title = "My edit" });
        var upsert = await recipeService.UpsertFromSourceAsync(new CreateRecipeDto
        {
            BeanId = bean.Id,
            BrewMethod = BrewMethod.V60,
            Source = RecipeSource.RoasterSite,
            Title = "Remote replacement"
        });
        Assert.Equal("My edit", upsert.Title);

        await beanService.DeleteBeanAsync(bean.Id);
        Assert.False(store.Recipes.Single(item => item.Id == recipe.Id).IsDeleted);
    }

    [Fact]
    public async Task Mutations_RaiseReferenceChangeNotifications()
    {
        using var store = new SqliteDataStore(DatabasePath(), seedOnFirstRun: false);
        var notifier = new DataChangeNotifier();
        var beans = new InMemoryBeanService(store, notifier, new InMemoryRatingService(store));
        DataChangedEventArgs? observed = null;
        notifier.DataChanged += (_, args) => observed = args;

        await beans.CreateBeanAsync(new CreateBeanDto { Name = "Observed" });
        Assert.NotNull(observed);
        Assert.Equal(RealDataChangeType.BeanCreated, observed!.ChangeType);
    }

    [Fact]
    public void NewerSchema_IsRejectedWithoutResettingState()
    {
        var path = DatabasePath();
        using (var database = new SqliteNativeDatabase(path))
        {
            database.Execute(
                "CREATE TABLE IF NOT EXISTS AppState(" +
                "Id INTEGER PRIMARY KEY CHECK(Id = 1), SchemaVersion INTEGER NOT NULL, Payload TEXT NOT NULL, UpdatedAt TEXT NOT NULL);");
            database.WriteState(SqliteDataStore.CurrentSchemaVersion + 1, "{}");
        }

        Assert.Throws<NotSupportedException>(() => new SqliteDataStore(path, seedOnFirstRun: false));
        using var databaseAfterFailure = new SqliteNativeDatabase(path);
        Assert.Equal(SqliteDataStore.CurrentSchemaVersion + 1, databaseAfterFailure.ReadState()!.Value.Version);
    }

    [Fact]
    public void NativeSqliteProvider_IsPortableAndUsable()
    {
        // Test project targets net11.0 (desktop), so NativeLibraryName uses the desktop path
        var expected = OperatingSystem.IsWindows() ? "winsqlite3" : "sqlite3";
        Assert.Equal(expected, SqliteNativeDatabase.NativeLibraryName);
        using var database = new SqliteNativeDatabase(DatabasePath());
        database.Execute("CREATE TABLE IF NOT EXISTS PortabilityProbe(Id INTEGER PRIMARY KEY);");
    }

    [Fact]
    public void NativeSqliteProvider_WindowsUsesSystemWinsqlite3()
    {
        Assert.Equal("winsqlite3", SqliteNativeDatabase.NativeLibraryNameFor(isWindows: true, isAndroid: false));
    }

    [Fact]
    public void NativeSqliteProvider_AndroidUsesBundledE_sqlite3()
    {
        // Android runtime bypasses SetDllImportResolver, so the DllImport constant
        // must be the actual library name (e_sqlite3, bundled by SQLitePCLRaw).
        Assert.Equal("e_sqlite3", SqliteNativeDatabase.NativeLibraryNameFor(isWindows: false, isAndroid: true));
    }

    [Fact]
    public void NativeSqliteProvider_DesktopUsesSystemSqlite3()
    {
        Assert.Equal("sqlite3", SqliteNativeDatabase.NativeLibraryNameFor(isWindows: false, isAndroid: false));
    }

    [Fact]
    public void SqliteCreateAndReopen_PersistentRelaunchPreservesData()
    {
        var path = DatabasePath();
        int shotId;

        // First "launch": create data
        using (var store = new SqliteDataStore(path))
        {
            Assert.True(File.Exists(path), "Database file must exist on disk after first open.");
            shotId = store.Shots[0].Id;
        }

        // Second "launch": reopen same database path — data must survive
        using (var store = new SqliteDataStore(path))
        {
            Assert.Equal(2, store.Beans.Count);
            Assert.Single(store.Shots);
            Assert.Equal(shotId, store.Shots[0].Id);
            Assert.NotNull(store.Shots[0].Bag);
            Assert.Equal("Ethiopian Yirgacheffe", store.Shots[0].Bag!.Bean!.Name);
        }

        // Third "launch": reopen again — still stable
        using var final = new SqliteDataStore(path);
        Assert.Equal(2, final.Beans.Count);
        Assert.Single(final.Shots);
    }

    [Fact]
    public void SqliteConstructor_RequiresValidPath_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => new SqliteDataStore(""));
        Assert.Throws<ArgumentException>(() => new SqliteDataStore("   "));
    }

    [Fact]
    public void PendingAvatarCleanup_PersistsAcrossReopen()
    {
        var path = DatabasePath();
        using (var store = new SqliteDataStore(path, seedOnFirstRun: false))
        {
            store.ExecuteMutation(() =>
            {
                store.PendingAvatarCleanups.Add(new CometBaristaNotes.Models.PendingAvatarCleanup
                {
                    ProfileId = 12,
                    AvatarPath = "profile_avatar_12_deadbeef.jpg",
                    Attempts = 2,
                    LastAttemptAt = new DateTime(2026, 9, 4, 20, 0, 0, DateTimeKind.Utc),
                    LastError = "Disk busy"
                });
                store.SaveChanges();
            });
        }

        using var reopened = new SqliteDataStore(path, seedOnFirstRun: false);
        var pending = Assert.Single(reopened.PendingAvatarCleanups);
        Assert.Equal(12, pending.ProfileId);
        Assert.Equal("profile_avatar_12_deadbeef.jpg", pending.AvatarPath);
        Assert.Equal(2, pending.Attempts);
        Assert.Equal("Disk busy", pending.LastError);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
            return;
        foreach (var file in Directory.GetFiles(_directory))
            File.Delete(file);
        Directory.Delete(_directory);
    }

    private string DatabasePath()
    {
        Directory.CreateDirectory(_directory);
        return IOPath.Combine(_directory, "baristanotes.db");
    }

    private async Task<(int BeanId, int CurrentShotId, int BestShotId)> CreateDeletedBagHistoryAsync()
    {
        using var store = new SqliteDataStore(DatabasePath(), seedOnFirstRun: false);
        var notifier = new DataChangeNotifier();
        var bean = (await new InMemoryBeanService(store, notifier, new InMemoryRatingService(store))
            .CreateBeanAsync(new CreateBeanDto { Name = "Deleted bag history" })).Data!;
        var bag = (await new InMemoryBagService(store, notifier)
            .CreateNewBagForBeanAsync(bean.Id, new DateTime(2026, 9, 1))).Data!;
        var shots = new InMemoryShotService(store, notifier);
        var current = await shots.CreateShotAsync(Shot(bag.Id, new DateTime(2026, 9, 3), 3));
        var best = await shots.CreateShotAsync(Shot(bag.Id, new DateTime(2026, 9, 2), 4));
        var deleted = await shots.CreateShotAsync(Shot(bag.Id, new DateTime(2026, 9, 4), 4));
        await shots.DeleteShotAsync(deleted.Id);
        await new InMemoryBagService(store, notifier).DeleteBagAsync(bag.Id);
        return (bean.Id, current.Id, best.Id);
    }

    private static CreateShotDto Shot(int bagId, DateTime timestamp, int rating) => new()
    {
        BagId = bagId,
        Timestamp = timestamp,
        BrewMethod = BrewMethod.Espresso,
        DoseIn = 18m,
        ExpectedTime = 28m,
        ExpectedOutput = 36m,
        DrinkType = "Espresso",
        Rating = rating
    };
}
