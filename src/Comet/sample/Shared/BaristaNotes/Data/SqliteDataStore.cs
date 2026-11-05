using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CometBaristaNotes.Models;
using CometBaristaNotes.Services;

namespace CometBaristaNotes.Data;

public sealed class SqliteDataStore : InMemoryDataStore
{
    public const int CurrentSchemaVersion = 1;
    private readonly SqliteNativeDatabase _database;

    public string DatabasePath { get; }
    public int SchemaVersion { get; private set; }

    public SqliteDataStore(string databasePath, bool seedOnFirstRun = true)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A database path is required.", nameof(databasePath));

        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _database = new SqliteNativeDatabase(DatabasePath);
        _database.Execute(
            "CREATE TABLE IF NOT EXISTS AppState(" +
            "Id INTEGER PRIMARY KEY CHECK(Id = 1), SchemaVersion INTEGER NOT NULL, Payload TEXT NOT NULL, UpdatedAt TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS Preferences(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);");

        var state = _database.ReadState();
        if (state is null)
        {
            SchemaVersion = CurrentSchemaVersion;
            if (seedOnFirstRun)
                Seed();
            else
                SaveChanges();
            return;
        }

        if (state.Value.Version > CurrentSchemaVersion)
            throw new NotSupportedException(
                $"Database schema {state.Value.Version} is newer than supported schema {CurrentSchemaVersion}.");

        SchemaVersion = state.Value.Version;
        Load(state.Value.Payload);
        Migrate();
    }

    public static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BaristaNotes",
            "baristanotes.db");

    public override void SaveChanges()
    {
        var state = new PersistedState
        {
            Beans = Beans,
            Bags = Bags,
            Shots = Shots,
            Equipment = Equipment,
            Profiles = Profiles,
            ShotEquipments = ShotEquipments,
            Recipes = Recipes,
            GrinderProfiles = GrinderProfiles,
            GrindTranslationCache = GrindTranslationCache,
            PendingAvatarCleanups = PendingAvatarCleanups
        };
        _database.WriteState(CurrentSchemaVersion, JsonSerializer.Serialize(state, BaristaJsonContext.Default.PersistedState));
        SchemaVersion = CurrentSchemaVersion;
    }

    public IPreferencesStore CreatePreferencesStore() => new SqlitePreferencesStore(_database);

    public override void Dispose() => _database.Dispose();

    private void Load(string payload)
    {
        var state = JsonSerializer.Deserialize(payload, BaristaJsonContext.Default.PersistedState)
            ?? throw new InvalidDataException("The BaristaNotes database state is empty.");
        Beans.AddRange(state.Beans);
        Bags.AddRange(state.Bags);
        Shots.AddRange(state.Shots);
        Equipment.AddRange(state.Equipment);
        Profiles.AddRange(state.Profiles);
        ShotEquipments.AddRange(state.ShotEquipments);
        Recipes.AddRange(state.Recipes);
        GrinderProfiles.AddRange(state.GrinderProfiles);
        GrindTranslationCache.AddRange(state.GrindTranslationCache);
        PendingAvatarCleanups.AddRange(state.PendingAvatarCleanups);
        RehydrateNavigation();
        AdvanceIds();
    }

    private void Migrate()
    {
        if (SchemaVersion == CurrentSchemaVersion)
            return;

        SchemaVersion = CurrentSchemaVersion;
        SaveChanges();
    }

    private void RehydrateNavigation()
    {
        foreach (var bag in Bags)
            bag.Bean = Beans.FirstOrDefault(bean => bean.Id == bag.BeanId);
        foreach (var shot in Shots)
        {
            shot.Bag = Bags.FirstOrDefault(bag => bag.Id == shot.BagId);
            shot.Machine = Equipment.FirstOrDefault(item => item.Id == shot.MachineId);
            shot.Grinder = Equipment.FirstOrDefault(item => item.Id == shot.GrinderId);
            shot.MadeBy = Profiles.FirstOrDefault(profile => profile.Id == shot.MadeById);
            shot.MadeFor = Profiles.FirstOrDefault(profile => profile.Id == shot.MadeForId);
            shot.ShotEquipments = ShotEquipments.Where(item => item.ShotRecordId == shot.Id).ToList();
        }
        foreach (var link in ShotEquipments)
        {
            link.ShotRecord = Shots.FirstOrDefault(shot => shot.Id == link.ShotRecordId);
            link.Equipment = Equipment.FirstOrDefault(item => item.Id == link.EquipmentId);
        }
        foreach (var recipe in Recipes)
            recipe.Bean = Beans.FirstOrDefault(bean => bean.Id == recipe.BeanId);
        foreach (var profile in GrinderProfiles)
            profile.Equipment = Equipment.FirstOrDefault(item => item.Id == profile.EquipmentId);
    }

    private void AdvanceIds()
    {
        SetNextIds(
            Beans.Select(item => item.Id).DefaultIfEmpty().Max() + 1,
            Bags.Select(item => item.Id).DefaultIfEmpty().Max() + 1,
            Shots.Select(item => item.Id).DefaultIfEmpty().Max() + 1,
            Equipment.Select(item => item.Id).DefaultIfEmpty().Max() + 1,
            Profiles.Select(item => item.Id).DefaultIfEmpty().Max() + 1,
            Recipes.Select(item => item.Id).DefaultIfEmpty().Max() + 1,
            GrinderProfiles.Select(item => item.Id).DefaultIfEmpty().Max() + 1,
            GrindTranslationCache.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
    }
}

internal sealed class PersistedState
{
    public System.Collections.Generic.List<Bean> Beans { get; init; } = [];
    public System.Collections.Generic.List<Bag> Bags { get; init; } = [];
    public System.Collections.Generic.List<ShotRecord> Shots { get; init; } = [];
    public System.Collections.Generic.List<Equipment> Equipment { get; init; } = [];
    public System.Collections.Generic.List<UserProfile> Profiles { get; init; } = [];
    public System.Collections.Generic.List<ShotEquipment> ShotEquipments { get; init; } = [];
    public System.Collections.Generic.List<Recipe> Recipes { get; init; } = [];
    public System.Collections.Generic.List<GrinderProfile> GrinderProfiles { get; init; } = [];
    public System.Collections.Generic.List<GrindTranslationCache> GrindTranslationCache { get; init; } = [];
    public System.Collections.Generic.List<PendingAvatarCleanup> PendingAvatarCleanups { get; init; } = [];
}

[JsonSerializable(typeof(PersistedState))]
[JsonSerializable(typeof(System.Collections.Generic.List<int>))]
[JsonSerializable(typeof(DrinkValueRangeSettings))]
internal partial class BaristaJsonContext : JsonSerializerContext
{
}
