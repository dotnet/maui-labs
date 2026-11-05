using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using BaristaComparison;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometSamples.BaristaNotes;
using CometSamples.BaristaNotes.Styles;

namespace CometBaristaNotes.Comparison;

public sealed class CometFixtureAdapter(string databasePath, BaristaServices? writer = null) : IFixtureAdapter
{
    readonly string _databasePath = BaristaAppStorage.ResolvePath(databasePath);

    static readonly (string Key, DrinkValueMetric Metric)[] Metrics =
    [
        ("dose", DrinkValueMetric.DoseIn),
        ("grind", DrinkValueMetric.GrindMicrons),
        ("output", DrinkValueMetric.Yield),
        ("time", DrinkValueMetric.Time),
    ];

    BaristaServices Writer => writer?.Store is SqliteDataStore store &&
        string.Equals(BaristaAppStorage.ResolvePath(store.DatabasePath), _databasePath, StringComparison.Ordinal)
            ? writer
            : throw new InvalidOperationException("A writer for the exact selected SQLite store is required.");

    public async Task<string> CreateBeanAsync(FixtureBean value)
    {
        var result = await Writer.BeanService.CreateBeanAsync(new CreateBeanDto
        {
            Name = value.Name, Roaster = value.Roaster, Origin = value.Origin, Notes = value.Notes,
        });
        if (!result.Success || result.Data is null)
            throw new InvalidOperationException($"Create bean {value.Key}: {result.ErrorMessage}");
        return Native(result.Data.Id);
    }

    public async Task<string> CreateBagAsync(FixtureBag value, FixtureIds ids)
    {
        if (value.IsComplete)
            throw new InvalidDataException("Fixture bags must be active when drinks are created.");
        var result = await Writer.BagService.CreateNewBagForBeanAsync(
            Id(ids.Beans, value.BeanKey), FixtureValidation.CalendarDate(value.RoastDate), value.Notes);
        if (!result.Success || result.Data is null)
            throw new InvalidOperationException($"Create bag {value.Key}: {result.ErrorMessage}");
        return Native(result.Data.Id);
    }

    public async Task<string> CreateEquipmentAsync(FixtureEquipment value)
    {
        var created = await Writer.EquipmentService.CreateEquipmentAsync(new CreateEquipmentDto
        {
            Name = value.Name, Type = ParseEnum<EquipmentType>(value.Type), Notes = value.Notes,
        });
        return Native(created.Id);
    }

    public async Task<string> CreatePersonAsync(FixturePerson value) =>
        Native((await Writer.ProfileService.CreateProfileAsync(
            new CreateUserProfileDto { Name = value.Name })).Id);

    public async Task<string> CreateDrinkAsync(FixtureDrink value, FixtureIds ids)
    {
        var created = await Writer.ShotService.CreateShotAsync(new CreateShotDto
        {
            Timestamp = FixtureValidation.Utc(value.TimestampUtc),
            BagId = Id(ids.Bags, value.BagKey),
            MachineId = Id(ids.Equipment, value.MachineKey),
            GrinderId = Id(ids.Equipment, value.GrinderKey),
            AccessoryIds = value.AccessoryKeys.Select(key => Id(ids.Equipment, key)).ToList(),
            MadeById = Id(ids.People, value.MadeByKey),
            MadeForId = Id(ids.People, value.MadeForKey),
            BrewMethod = ParseEnum<BrewMethod>(value.BrewMethod),
            DrinkType = value.DrinkType, DoseIn = value.DoseIn,
            GrindMicrons = value.GrindMicrons, WaterTempC = value.WaterTempC,
            ExpectedTime = value.ExpectedTime, ExpectedOutput = value.ExpectedOutput,
            ActualTime = value.ActualTime, ActualOutput = value.ActualOutput,
            PreinfusionTime = value.PreinfusionTime, Rating = value.Rating, TastingNotes = value.TastingNotes,
        });
        return Native(created.Id);
    }

    public Task ApplyPreferencesAsync(FixtureDefinition fixture, FixtureIds ids)
    {
        var preferences = Writer.Preferences;
        preferences.SetTemperatureUnit(ParseEnum<TemperatureUnit>(fixture.Preferences.TemperatureUnit));
        Writer.ThemePreferences.Save(ParseEnum<CoffeeThemeMode>(fixture.Preferences.Theme));
        foreach (var (_, metric) in Metrics)
            Writer.RangeService.SetMode(metric, ParseEnum<ValueRangeMode>(fixture.Preferences.ValueRanges));

        var latest = fixture.Drinks[^1];
        var bag = fixture.Bags.Single(value => value.Key == latest.BagKey);
        preferences.SetLastDrinkType(latest.DrinkType);
        preferences.SetLastBeanId(Id(ids.Beans, bag.BeanKey));
        preferences.SetLastBagId(Id(ids.Bags, latest.BagKey));
        preferences.SetLastMachineId(Id(ids.Equipment, latest.MachineKey));
        preferences.SetLastGrinderId(Id(ids.Equipment, latest.GrinderKey));
        preferences.SetLastAccessoryIds(latest.AccessoryKeys.Select(key => Id(ids.Equipment, key)).ToList());
        preferences.SetLastMadeById(Id(ids.People, latest.MadeByKey));
        preferences.SetLastMadeForId(Id(ids.People, latest.MadeForKey));
        preferences.SetLastDoseIn(latest.DoseIn);
        preferences.SetLastGrindMicrons(latest.GrindMicrons);
        preferences.SetLastExpectedTime(latest.ExpectedTime);
        preferences.SetLastExpectedOutput(latest.ExpectedOutput);
        preferences.SetLastPreinfusionTime(latest.PreinfusionTime);
        return Task.CompletedTask;
    }

    public async Task<FixtureReadback> ReadAsync(FixtureIds ids)
    {
        if (!File.Exists(_databasePath))
            throw new InvalidDataException("The selected fixture database is missing.");

        // A fresh store reloads the persisted snapshot. Reader services do not bind the app locator.
        using var store = new SqliteDataStore(_databasePath, seedOnFirstRun: false);
        var notifier = new DataChangeNotifier();
        var preferences = new PreferencesService(store.CreatePreferencesStore());
        var ranges = new InMemoryDrinkValueRangeService(preferences);
        if (ranges.GetSettings().LoadWarning is { } warning)
            throw new InvalidDataException("Persisted fixture range settings cannot be validated: " + warning);
        var beans = new InMemoryBeanService(store, notifier, new InMemoryRatingService(store));
        var bags = new InMemoryBagService(store, notifier);
        var equipment = new InMemoryEquipmentService(store, notifier);
        var people = new InMemoryUserProfileService(store, notifier, new LocalImageProcessingService());
        var shots = new InMemoryShotService(store, notifier, preferences);
        var theme = new BaristaThemePreferences(store.CreatePreferencesStore());

        var actualBeans = new List<FixtureBean>();
        var actualBags = new List<FixtureBag>();
        foreach (var item in await beans.GetAllActiveBeansAsync())
        {
            var actual = VerifyBeanPair(item, Required(await beans.GetBeanByIdAsync(item.Id)), ids);
            actualBeans.Add(actual);
            foreach (var bag in await bags.GetBagsForBeanAsync(item.Id, includeCompleted: true))
            {
                var actualBag = VerifyBagPair(bag, Required(await bags.GetBagByIdAsync(bag.Id)), ids);
                actualBags.Add(actualBag);
            }
        }

        var actualEquipment = new List<FixtureEquipment>();
        foreach (var item in await equipment.GetAllActiveEquipmentAsync())
        {
            var actual = VerifyEquipmentPair(item, Required(await equipment.GetEquipmentByIdAsync(item.Id)), ids);
            actualEquipment.Add(actual);
        }

        var actualPeople = new List<FixturePerson>();
        foreach (var item in await people.GetAllProfilesAsync())
        {
            var actual = VerifyPersonPair(item, Required(await people.GetProfileByIdAsync(item.Id)), ids);
            actualPeople.Add(actual);
        }

        var actualDrinks = new List<FixtureDrink>();
        var totalCount = -1;
        for (var pageIndex = 0; ; pageIndex++)
        {
            var page = await shots.GetShotHistoryAsync(pageIndex, 100);
            if (totalCount == -1)
                totalCount = page.TotalCount;
            if (page.TotalCount != totalCount || (page.Items.Count == 0 && page.HasNextPage))
                throw new InvalidDataException("History changed during readback.");
            foreach (var item in page.Items)
            {
                var actual = Drink(item, ids);
                Same(actual, Drink(Required(await shots.GetShotByIdAsync(item.Id)), ids), FixtureJson.Default.FixtureDrink);
                actualDrinks.Add(actual);
            }
            if (!page.HasNextPage)
                break;
        }
        if (actualDrinks.Count != totalCount)
            throw new InvalidDataException("History count differs from persisted page results.");

        var latest100 = await shots.GetShotHistoryAsync(0, 100);
        var recentShot = await shots.GetMostRecentShotAsync();
        if (actualDrinks.Count > 0)
            Same(actualDrinks[0], Drink(Required(recentShot), ids), FixtureJson.Default.FixtureDrink);
        else if (recentShot is not null)
            throw new InvalidDataException("Most recent drink differs from history.");

        var recent = new RecentSelections(
            preferences.GetLastDrinkType(),
            OptionalKey(ids.Beans, preferences.GetLastBeanId()),
            OptionalKey(ids.Bags, preferences.GetLastBagId()),
            OptionalKey(ids.Equipment, preferences.GetLastMachineId()),
            OptionalKey(ids.Equipment, preferences.GetLastGrinderId()),
            preferences.GetLastAccessoryIds().Select(id => Key(ids.Equipment, id)).ToArray(),
            OptionalKey(ids.People, preferences.GetLastMadeById()),
            OptionalKey(ids.People, preferences.GetLastMadeForId()),
            preferences.GetLastDoseIn(), preferences.GetLastGrindMicrons(),
            preferences.GetLastExpectedTime(), preferences.GetLastExpectedOutput(),
            preferences.GetLastPreinfusionTime());

        return new FixtureReadback(
            actualBeans.ToArray(), actualBags.ToArray(), actualEquipment.ToArray(), actualPeople.ToArray(),
            actualDrinks.ToArray(),
            new PersistedPreferences(preferences.GetTemperatureUnit().ToString(), theme.Load().ToString(),
                new SortedDictionary<string, string>(
                    Metrics.ToDictionary(item => item.Key, item => ranges.GetMode(item.Metric).ToString()),
                    StringComparer.Ordinal)),
            recent,
            new EntityCounts(actualBeans.Count, actualBags.Count, actualEquipment.Count, actualPeople.Count, totalCount),
            actualDrinks.Count(item => item.Rating == 0),
            actualDrinks.Select(item => item.Key).ToArray(),
            latest100.Items.Select(item => Key(ids.Drinks, item.Id)).ToArray());
    }

    internal static FixtureBean VerifyBeanPair(BeanDto listed, BeanDto byId, FixtureIds ids) =>
        Matched(listed.Id, byId.Id, Bean(listed, ids), Bean(byId, ids), FixtureJson.Default.FixtureBean);

    internal static FixtureBag VerifyBagPair(Bag listed, Bag byId, FixtureIds ids) =>
        Matched(listed.Id, byId.Id, BagValue(listed, ids), BagValue(byId, ids), FixtureJson.Default.FixtureBag);

    internal static FixtureEquipment VerifyEquipmentPair(EquipmentDto listed, EquipmentDto byId, FixtureIds ids) =>
        Matched(listed.Id, byId.Id, Equipment(listed, ids), Equipment(byId, ids), FixtureJson.Default.FixtureEquipment);

    internal static FixturePerson VerifyPersonPair(UserProfileDto listed, UserProfileDto byId, FixtureIds ids) =>
        Matched(listed.Id, byId.Id, Person(listed, ids), Person(byId, ids), FixtureJson.Default.FixturePerson);

    static T Matched<T>(int listedId, int byId, T listed, T fetched, JsonTypeInfo<T> type)
    {
        if (listedId != byId)
            throw new InvalidDataException($"List and by-ID native IDs disagree for {typeof(T).Name}.");
        Same(listed, fetched, type);
        return listed;
    }

    static FixtureBean Bean(BeanDto item, FixtureIds ids) =>
        new(Key(ids.Beans, item.Id), item.Name, item.Roaster, item.Origin, item.Notes);

    static FixtureBag BagValue(Bag item, FixtureIds ids) =>
        new(Key(ids.Bags, item.Id), Key(ids.Beans, item.BeanId),
            item.RoastDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), item.Notes, item.IsComplete);

    static FixtureEquipment Equipment(EquipmentDto item, FixtureIds ids) =>
        new(Key(ids.Equipment, item.Id), item.Name, item.Type.ToString(), item.Notes);

    static FixturePerson Person(UserProfileDto item, FixtureIds ids) =>
        new(Key(ids.People, item.Id), item.Name);

    static FixtureDrink Drink(ShotRecordDto item, FixtureIds ids) =>
        new(Key(ids.Drinks, item.Id), FixtureValidation.FixtureUtc(item.Timestamp),
            Key(ids.Bags, Required(item.Bag).Id), Key(ids.Equipment, Required(item.Machine).Id),
            Key(ids.Equipment, Required(item.Grinder).Id),
            item.Accessories.Select(value => Key(ids.Equipment, value.Id)).ToArray(),
            Key(ids.People, Required(item.MadeBy).Id), Key(ids.People, Required(item.MadeFor).Id),
            item.BrewMethod.ToString(), item.DrinkType, item.DoseIn, item.GrindMicrons, item.WaterTempC,
            item.ExpectedTime, item.ExpectedOutput, item.ActualTime, item.ActualOutput,
            item.PreinfusionTime, item.Rating, item.TastingNotes);

    static T Required<T>(T? value) where T : class =>
        value ?? throw new InvalidDataException("A persisted fixture entity or relationship is missing.");

    static string Native(int id) => id > 0
        ? id.ToString(CultureInfo.InvariantCulture)
        : throw new InvalidDataException("The domain service returned a non-positive native ID.");

    static int Id(Dictionary<string, string> ids, string key)
    {
        if (!ids.TryGetValue(key, out var value) ||
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            id <= 0 || value != Native(id))
            throw new InvalidDataException($"Invalid or missing native ID for {key}.");
        return id;
    }

    static string Key(Dictionary<string, string> ids, int id)
    {
        var native = Native(id);
        var matches = ids.Where(pair => pair.Value == native).Select(pair => pair.Key).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidDataException($"Native ID {native} has no unique logical key.");
    }

    static string? OptionalKey(Dictionary<string, string> ids, int? id) =>
        id.HasValue ? Key(ids, id.Value) : null;

    static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(parsed) && parsed.ToString() == value
            ? parsed : throw new InvalidDataException($"Unsupported {typeof(T).Name}: {value}.");

    static void Same<T>(T listed, T byId, JsonTypeInfo<T> type)
    {
        if (!JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(listed, type),
                JsonSerializer.SerializeToElement(byId, type)))
            throw new InvalidDataException($"List and by-ID readback disagree for {typeof(T).Name}.");
    }
}
