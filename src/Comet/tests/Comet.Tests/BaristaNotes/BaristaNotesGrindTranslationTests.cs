using System;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.Grind;
using Xunit;
using RealEquipmentType = CometBaristaNotes.Models.Enums.EquipmentType;

namespace Comet.Tests.BaristaNotes.Domain;

public class BaristaNotesGrindTranslationTests
{
    [Fact]
    public async Task TranslateAsync_HistoryOverridesRecipeHintAndUsesMostRecentShot()
    {
        var store = SeededStore();
        var grinder = store.Equipment.Single(item => item.Type == RealEquipmentType.Grinder);
        var bag = store.Bags.First();
        store.Shots.Add(new CometBaristaNotes.Models.ShotRecord
        {
            Id = store.NextShotId(),
            BagId = bag.Id,
            GrinderId = grinder.Id,
            BrewMethod = BrewMethod.Espresso,
            GrindMicrons = 700,
            Timestamp = new DateTime(2026, 2, 1),
            DrinkType = "Espresso"
        });
        var service = new InMemoryGrindTranslationService(store);

        var result = await service.TranslateAsync(new(
            grinder.Id,
            grinder.Name,
            "1000µm",
            BrewMethod.Espresso,
            bag.BeanId));

        Assert.Equal(GrindTranslationSource.UserHistory, result.Source);
        Assert.Equal(60m, result.SuggestedSetting);
        Assert.Contains("last espresso", result.Explanation);
    }

    [Fact]
    public async Task TranslateAsync_PersistedValidProfilePrecedesKnownSeed()
    {
        var store = SeededStore();
        var grinder = store.Equipment.Single(item => item.Type == RealEquipmentType.Grinder);
        store.GrinderProfiles.Add(new CometBaristaNotes.Models.GrinderProfile
        {
            Id = store.NextGrinderProfileId(),
            EquipmentId = grinder.Id,
            AnchorsJson =
                "[{\"micron\":500,\"setting\":10,\"source\":\"user\"}," +
                "{\"micron\":700,\"setting\":20,\"source\":\"user\"}]"
        });
        var beanWithoutHistory = store.Beans.Last();

        var result = await new InMemoryGrindTranslationService(store).TranslateAsync(new(
            grinder.Id,
            grinder.Name,
            "600µm",
            BrewMethod.Espresso,
            beanWithoutHistory.Id));

        Assert.Equal(GrindTranslationSource.Deterministic, result.Source);
        Assert.Equal(15m, result.SuggestedSetting);
        Assert.Contains("calibration anchors", result.Explanation);
    }

    [Fact]
    public async Task TranslateAsync_DefaultSeededDf64vWorksWithoutPersistedProfile()
    {
        var store = SeededStore();
        var grinder = store.Equipment.Single(item => item.Type == RealEquipmentType.Grinder);
        var beanWithoutHistory = store.Beans.Last();

        var result = await new InMemoryGrindTranslationService(store).TranslateAsync(new(
            grinder.Id,
            "Turin DF64V",
            "600 microns",
            BrewMethod.Espresso,
            beanWithoutHistory.Id));

        Assert.Empty(store.GrinderProfiles);
        Assert.Equal(GrindTranslationSource.Deterministic, result.Source);
        Assert.Equal(50m, result.SuggestedSetting);
        Assert.Contains("built-in DF64", result.Explanation);
    }

    [Fact]
    public async Task TranslateAsync_InvalidProfileFallsBackToKnownSeed()
    {
        var store = SeededStore();
        var grinder = store.Equipment.Single(item => item.Type == RealEquipmentType.Grinder);
        store.GrinderProfiles.Add(new CometBaristaNotes.Models.GrinderProfile
        {
            Id = store.NextGrinderProfileId(),
            EquipmentId = grinder.Id,
            AnchorsJson =
                "[{\"micron\":600,\"setting\":12,\"source\":\"user\"}," +
                "{\"micron\":0,\"setting\":20,\"source\":\"invalid\"}]"
        });

        var result = await new InMemoryGrindTranslationService(store).TranslateAsync(new(
            grinder.Id,
            "MiiCoffee DF64 Gen 2",
            "600µm",
            BrewMethod.V60,
            store.Beans.Last().Id));

        Assert.Equal(50m, result.SuggestedSetting);
        Assert.Contains("built-in DF64", result.Explanation);
    }

    [Fact]
    public async Task TranslateAsync_UnknownGrinderWithoutProfileReturnsSetupGuidance()
    {
        var store = new InMemoryDataStore();
        var service = new InMemoryGrindTranslationService(store);

        var result = await service.TranslateAsync(new(
            null,
            "Unknown Grinder",
            "medium-fine",
            BrewMethod.V60,
            null));

        Assert.Equal(GrindTranslationSource.Default, result.Source);
        Assert.Null(result.SuggestedSetting);
        Assert.Contains("two grinder calibration anchors", result.Explanation);
    }

    [Fact]
    public void Interpolate_UnsortedAnchors_IsLinearAndClampedToProfileBounds()
    {
        GrindAnchor[] anchors =
        [
            new(700m, 20m, "user"),
            new(500m, 10m, "user"),
            new(0m, 999m, "invalid")
        ];

        var result = DeterministicGrindInterpolator.Interpolate(
            anchors,
            600m,
            (500m, 700m),
            minSetting: 12m,
            maxSetting: 18m);

        Assert.NotNull(result);
        Assert.Equal(12m, result!.Min);
        Assert.Equal(18m, result.Max);
        Assert.Equal(15m, result.Suggested);
        Assert.Equal(2, result.AnchorsUsed);
    }

    [Fact]
    public void ServiceLocator_ExposesSharedGrindTranslationContract()
    {
        var store = SeededStore();
        BaristaServiceLocator.Initialize(
            store,
            new DataChangeNotifier(),
            new InMemoryPreferencesService(),
            new RecordingImageProcessingService());

        Assert.IsAssignableFrom<IGrindTranslationService>(BaristaServiceLocator.GrindTranslationService);
    }

    private static InMemoryDataStore SeededStore()
    {
        var store = new InMemoryDataStore();
        store.Seed();
        return store;
    }
}
