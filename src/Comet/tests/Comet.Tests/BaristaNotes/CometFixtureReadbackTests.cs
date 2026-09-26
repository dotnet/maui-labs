#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaristaComparison;
using CometBaristaNotes.Comparison;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometSamples.BaristaNotes;
using CometSamples.BaristaNotes.Styles;
using Comet.Tests.VoiceDefects;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaFixtures;

[Collection(VoiceIntegrationCollection.Name)]
public sealed class CometFixtureReadbackTests
{
    static readonly DrinkValueMetric[] Metrics =
        [DrinkValueMetric.DoseIn, DrinkValueMetric.GrindMicrons, DrinkValueMetric.Yield, DrinkValueMetric.Time];

    static string NewDirectory() => IOPath.Combine(
        AppContext.BaseDirectory, "artifacts", "comet-fixture-readback", Guid.NewGuid().ToString("N"));

    static FixtureIds PairIds() => new()
    {
        Beans = new() { ["bean-a"] = "1", ["bean-b"] = "2" },
        Bags = new() { ["bag-a"] = "1", ["bag-b"] = "2" },
        Equipment = new() { ["equipment-a"] = "1", ["equipment-b"] = "2" },
        People = new() { ["person-a"] = "1", ["person-b"] = "2" },
    };

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("roaster")]
    [InlineData("origin")]
    [InlineData("notes")]
    [InlineData("null-notes")]
    public void BeanPair_RejectsEveryCommonFieldMismatch(string field)
    {
        var listed = new BeanDto { Id = 1, Name = "Bean", Roaster = "Roaster", Origin = "Origin", Notes = "" };
        var byId = field switch
        {
            "id" => listed with { Id = 2 },
            "name" => listed with { Name = "Changed" },
            "roaster" => listed with { Roaster = "Changed" },
            "origin" => listed with { Origin = "Changed" },
            "notes" => listed with { Notes = "Changed" },
            "null-notes" => listed with { Notes = null },
            _ => throw new ArgumentException(field),
        };
        Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyBeanPair(listed, byId, PairIds()));
        Assert.Equal(new FixtureBean("bean-a", "Bean", "Roaster", "Origin", ""),
            CometFixtureAdapter.VerifyBeanPair(listed, listed with { CreatedAt = DateTime.UtcNow }, PairIds()));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("bean")]
    [InlineData("roast-date")]
    [InlineData("notes")]
    [InlineData("null-notes")]
    [InlineData("complete")]
    public void BagPair_RejectsEveryCommonFieldMismatch(string field)
    {
        static Bag Original() => new() { Id = 1, BeanId = 1, RoastDate = new DateTime(2026, 1, 1), Notes = "" };
        var listed = Original();
        var byId = Original();
        switch (field)
        {
            case "id": byId.Id = 2; break;
            case "bean": byId.BeanId = 2; break;
            case "roast-date": byId.RoastDate = byId.RoastDate.AddDays(1); break;
            case "notes": byId.Notes = "Changed"; break;
            case "null-notes": byId.Notes = null; break;
            case "complete": byId.IsComplete = true; break;
            default: throw new ArgumentException(field);
        }
        Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyBagPair(listed, byId, PairIds()));
        Assert.Equal(new FixtureBag("bag-a", "bean-a", "2026-01-01", "", false),
            CometFixtureAdapter.VerifyBagPair(listed, Original(), PairIds()));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("type")]
    [InlineData("notes")]
    [InlineData("null-notes")]
    public void EquipmentPair_RejectsEveryCommonFieldMismatch(string field)
    {
        var listed = new EquipmentDto { Id = 1, Name = "Machine", Type = EquipmentType.Machine, Notes = "" };
        var byId = field switch
        {
            "id" => listed with { Id = 2 },
            "name" => listed with { Name = "Changed" },
            "type" => listed with { Type = EquipmentType.Grinder },
            "notes" => listed with { Notes = "Changed" },
            "null-notes" => listed with { Notes = null },
            _ => throw new ArgumentException(field),
        };
        Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyEquipmentPair(listed, byId, PairIds()));
        Assert.Equal(new FixtureEquipment("equipment-a", "Machine", "Machine", ""),
            CometFixtureAdapter.VerifyEquipmentPair(listed, listed with { CreatedAt = DateTime.UtcNow }, PairIds()));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    public void PersonPair_RejectsEveryCommonFieldMismatch(string field)
    {
        var listed = new UserProfileDto { Id = 1, Name = "Person" };
        var byId = field == "id" ? listed with { Id = 2 } : listed with { Name = "Changed" };
        Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyPersonPair(listed, byId, PairIds()));
        Assert.Equal(new FixturePerson("person-a", "Person"),
            CometFixtureAdapter.VerifyPersonPair(listed, listed with { CreatedAt = DateTime.UtcNow }, PairIds()));
    }

    [Fact]
    public void SwappedByIdResponses_AreRejectedEvenWhenCanonicalSetsMatch()
    {
        var ids = PairIds();
        var beans = new[] { new BeanDto { Id = 1, Name = "A" }, new BeanDto { Id = 2, Name = "B" } };
        var bags = new[] { new Bag { Id = 1, BeanId = 1 }, new Bag { Id = 2, BeanId = 2 } };
        var equipment = new[] { new EquipmentDto { Id = 1, Name = "A" }, new EquipmentDto { Id = 2, Name = "B" } };
        var people = new[] { new UserProfileDto { Id = 1, Name = "A" }, new UserProfileDto { Id = 2, Name = "B" } };
        var baseline = new FixtureReadback(
            beans.Select(value => CometFixtureAdapter.VerifyBeanPair(value, value, ids)).ToArray(),
            bags.Select(value => CometFixtureAdapter.VerifyBagPair(value, value, ids)).ToArray(),
            equipment.Select(value => CometFixtureAdapter.VerifyEquipmentPair(value, value, ids)).ToArray(),
            people.Select(value => CometFixtureAdapter.VerifyPersonPair(value, value, ids)).ToArray(),
            [], new PersistedPreferences("Celsius", "Light", new SortedDictionary<string, string>()),
            new RecentSelections(null, null, null, null, null, [], null, null, null, null, null, null, null),
            new EntityCounts(2, 2, 2, 2, 0), 0, [], []);
        var reversed = baseline with
        {
            Beans = baseline.Beans.Reverse().ToArray(),
            Bags = baseline.Bags.Reverse().ToArray(),
            Equipment = baseline.Equipment.Reverse().ToArray(),
            People = baseline.People.Reverse().ToArray(),
        };
        Assert.Equal(FixtureValidation.Canonical(baseline), FixtureValidation.Canonical(reversed));
        for (var index = 0; index < 2; index++)
        {
            var other = 1 - index;
            Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyBeanPair(beans[index], beans[other], ids));
            Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyBagPair(bags[index], bags[other], ids));
            Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyEquipmentPair(equipment[index], equipment[other], ids));
            Assert.Throws<InvalidDataException>(() => CometFixtureAdapter.VerifyPersonPair(people[index], people[other], ids));
        }
    }

    [Fact]
    public async Task Readback_ReopensPersistedEntitiesRangesAndTheme_WithoutRefreshingWriterCaches()
    {
        using (var anchor = new InMemoryDataStore())
        {
            BaristaServiceLocator.Initialize(anchor, new DataChangeNotifier());
            BaristaServiceLocator.Reset(anchor);
        }
        var root = NewDirectory();
        var defaultPath = IOPath.Combine(root, "default.db");
        using (var normal = new SqliteDataStore(defaultPath))
            Assert.Single(normal.Shots);
        var defaultBytes = File.ReadAllBytes(defaultPath);
        var database = IOPath.Combine(root, "comparison", "barista_notes.db");
        var storage = new BaristaAppStorage(defaultPath, new CometFixtureTests.RejectPreferences());
        storage.ConfigureComparisonDatabase(database);
        using var writer = new BaristaServices(storage);
        var adapter = new CometFixtureAdapter(database, writer);
        var ids = new FixtureIds();
        ids.Beans.Add("bean", await adapter.CreateBeanAsync(new FixtureBean("bean", "Writer bean", "Old roaster", "Old origin", "Old notes")));
        ids.Bags.Add("bag", await adapter.CreateBagAsync(new FixtureBag("bag", "bean", "2026-01-01", "Old notes", false), ids));
        ids.Equipment.Add("equipment", await adapter.CreateEquipmentAsync(new FixtureEquipment("equipment", "Writer machine", "Machine", "Old notes")));
        ids.People.Add("person", await adapter.CreatePersonAsync(new FixturePerson("person", "Writer person")));
        writer.Preferences.SetTemperatureUnit(TemperatureUnit.Fahrenheit);
        writer.Preferences.SetLastDrinkType("Writer drink");
        writer.ThemePreferences.Save(CoffeeThemeMode.Dark);
        foreach (var metric in Metrics)
            writer.RangeService.SetMode(metric, ValueRangeMode.Custom);
        var first = await adapter.ReadAsync(ids);
        Assert.Equal("Dark", first.Preferences.Theme);
        Assert.All(first.Preferences.ValueRanges.Values, value => Assert.Equal("Custom", value));

        using (var persisted = new SqliteDataStore(database, seedOnFirstRun: false))
        {
            var notifier = new DataChangeNotifier();
            var preferences = new PreferencesService(persisted.CreatePreferencesStore());
            await new InMemoryBeanService(persisted, notifier, new InMemoryRatingService(persisted))
                .UpdateBeanAsync(Native(ids.Beans, "bean"), new UpdateBeanDto
                { Name = "Persisted bean", Roaster = "New roaster", Origin = "New origin", Notes = "New notes" });
            var bag = await new InMemoryBagService(persisted, notifier).UpdateBagAsync(new Bag
            {
                Id = Native(ids.Bags, "bag"), RoastDate = new DateTime(2026, 1, 2),
                Notes = "New notes", IsComplete = true,
            });
            Assert.True(bag.Success);
            await new InMemoryEquipmentService(persisted, notifier).UpdateEquipmentAsync(
                Native(ids.Equipment, "equipment"), new UpdateEquipmentDto
                { Name = "Persisted grinder", Type = EquipmentType.Grinder, Notes = "New notes" });
            await new InMemoryUserProfileService(persisted, notifier, new LocalImageProcessingService())
                .UpdateProfileAsync(Native(ids.People, "person"), new UpdateUserProfileDto { Name = "Persisted person" });
            preferences.SetTemperatureUnit(TemperatureUnit.Celsius);
            preferences.SetLastDrinkType("Persisted drink");
            new BaristaThemePreferences(persisted.CreatePreferencesStore()).Save(CoffeeThemeMode.Light);
            var ranges = new InMemoryDrinkValueRangeService(preferences);
            foreach (var metric in Metrics)
                ranges.SetMode(metric, ValueRangeMode.Auto);
        }

        var actual = await adapter.ReadAsync(ids);
        Assert.Equal(new FixtureBean("bean", "Persisted bean", "New roaster", "New origin", "New notes"), Assert.Single(actual.Beans));
        Assert.Equal(new FixtureBag("bag", "bean", "2026-01-02", "New notes", true), Assert.Single(actual.Bags));
        Assert.Equal(new FixtureEquipment("equipment", "Persisted grinder", "Grinder", "New notes"), Assert.Single(actual.Equipment));
        Assert.Equal(new FixturePerson("person", "Persisted person"), Assert.Single(actual.People));
        Assert.Equal("Celsius", actual.Preferences.TemperatureUnit);
        Assert.Equal("Light", actual.Preferences.Theme);
        Assert.Equal("Persisted drink", actual.Recent.DrinkType);
        Assert.All(actual.Preferences.ValueRanges.Values, value => Assert.Equal("Auto", value));
        Assert.Empty(actual.Drinks);

        Assert.Equal("Writer bean", Assert.Single(await writer.BeanService.GetAllActiveBeansAsync()).Name);
        Assert.False(Assert.Single(await writer.BagService.GetBagsForBeanAsync(Native(ids.Beans, "bean"))).IsComplete);
        Assert.Equal("Writer machine", Assert.Single(await writer.EquipmentService.GetAllActiveEquipmentAsync()).Name);
        Assert.Equal("Writer person", Assert.Single(await writer.ProfileService.GetAllProfilesAsync()).Name);
        Assert.All(Metrics, metric => Assert.Equal(ValueRangeMode.Custom, writer.RangeService.GetMode(metric)));
        Assert.Same(writer.RangeService, BaristaServiceLocator.RangeService);
        writer.Dispose();
        Assert.Equal(FixtureValidation.Canonical(actual), FixtureValidation.Canonical(await adapter.ReadAsync(ids)));
        Assert.Equal(defaultBytes, File.ReadAllBytes(defaultPath));
    }

    [Fact]
    public void NewNamespace_WithLiveSqliteWalAndTheme_IsRefusedWithoutChangingStorage()
    {
        var root = NewDirectory();
        var selection = new FixtureSelection("fixture-v2-100-livewal", 100, "provision");
        var directory = FixtureStore.NamespacePath(root, selection);
        using var orphan = new SqliteDataStore(IOPath.Combine(directory, "barista_notes.db"), seedOnFirstRun: false);
        var theme = new BaristaThemePreferences(orphan.CreatePreferencesStore());
        theme.Save(CoffeeThemeMode.Dark);
        Assert.True(new FileInfo(orphan.DatabasePath + "-wal").Length > 0);
        var before = Snapshot(directory);
        Assert.Throws<InvalidDataException>(() => CometFixtureSession.Preflight(root, selection, _ => false));
        Assert.Equal(before, Snapshot(directory));
        Assert.Equal(CoffeeThemeMode.Dark, theme.Load());
        Assert.False(File.Exists(IOPath.Combine(directory, "receipt.json")));
    }

    [Theory]
    [InlineData("barista_notes.db")]
    [InlineData("barista_notes.db-wal")]
    [InlineData("barista_notes.db-shm")]
    [InlineData("barista_notes.db-journal")]
    [InlineData("barista_notes.db.bak")]
    [InlineData("receipt.json.recovery.tmp")]
    public void NewNamespace_WithAnyRetainedStorage_IsRefusedBeforeOpeningOrReserving(string file)
    {
        var root = NewDirectory();
        var selection = new FixtureSelection("fixture-v2-100-orphan", 100, "provision");
        var directory = FixtureStore.NamespacePath(root, selection);
        Directory.CreateDirectory(directory);
        File.WriteAllText(IOPath.Combine(directory, file), "Retained data must not be opened or replaced.");
        var before = Snapshot(directory);
        Assert.Throws<InvalidDataException>(() => CometFixtureSession.Preflight(root, selection, _ => false));
        Assert.Equal(before, Snapshot(directory));
        Assert.False(File.Exists(IOPath.Combine(directory, "receipt.json")));
    }

    static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => IOPath.GetRelativePath(directory, path) + ":" + FixtureValidation.Hash(File.ReadAllBytes(path)))
        .ToArray();

    static int Native(Dictionary<string, string> ids, string key) => int.Parse(ids[key], CultureInfo.InvariantCulture);
}
