using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaristaComparison;

public sealed record FixtureBean(string Key, string Name, string? Roaster, string? Origin, string? Notes);
public sealed record FixtureBag(string Key, string BeanKey, string RoastDate, string? Notes, bool IsComplete);
public sealed record FixtureEquipment(string Key, string Name, string Type, string? Notes);
public sealed record FixturePerson(string Key, string Name);
public sealed record FixtureDrink(
    string Key, string TimestampUtc, string BagKey, string MachineKey, string GrinderKey,
    string[] AccessoryKeys, string MadeByKey, string MadeForKey, string BrewMethod, string DrinkType,
    decimal DoseIn, int? GrindMicrons, decimal? WaterTempC, decimal ExpectedTime, decimal ExpectedOutput,
    decimal? ActualTime, decimal? ActualOutput, decimal? PreinfusionTime, int? Rating, string? TastingNotes);
public sealed record FixturePreferences(string ActivityFilter, string TemperatureUnit, string Theme, string ValueRanges);
public sealed record FixtureDefinition(
    int Version, string Name, int DrinkCount, string ReferenceTimeUtc,
    FixtureBean[] Beans, FixtureBag[] Bags, FixtureEquipment[] Equipment, FixturePerson[] People,
    FixturePreferences Preferences, FixtureDrink[] Drinks);

public sealed record RecentSelections(
    string? DrinkType, string? BeanKey, string? BagKey, string? MachineKey, string? GrinderKey,
    string[] AccessoryKeys, string? MadeByKey, string? MadeForKey, decimal? DoseIn,
    int? GrindMicrons, decimal? ExpectedTime, decimal? ExpectedOutput, decimal? PreinfusionTime);
public sealed record PersistedPreferences(string TemperatureUnit, string Theme, SortedDictionary<string, string> ValueRanges);
public sealed record EntityCounts(int Beans, int Bags, int Equipment, int People, int Drinks);
public sealed record FixtureReadback(
    FixtureBean[] Beans, FixtureBag[] Bags, FixtureEquipment[] Equipment, FixturePerson[] People,
    FixtureDrink[] Drinks, PersistedPreferences Preferences, RecentSelections Recent,
    EntityCounts Counts, int RatingZeroCount, string[] HistoryNewestFirst, string[] Latest100);

public sealed class FixtureIds
{
    public Dictionary<string, string> Beans { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Bags { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Equipment { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> People { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Drinks { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ProvisioningReceipt
{
    public required string Namespace { get; init; }
    public required string FixtureName { get; init; }
    public required string InputSha256 { get; init; }
    public string State { get; set; } = "provisioning";
    public string? PendingOperation { get; set; }
    public string? Failure { get; set; }
    public string? SemanticSha256 { get; set; }
    public FixtureIds Ids { get; init; } = new();
    public string TimestampConvention { get; init; } =
        "Fixture UTC; SQLite Unspecified digits are UTC only in this namespace; roast date is a calendar date.";
}

public sealed record FixtureSelection(string Namespace, int DrinkCount, string Mode);

public interface IFixtureAdapter
{
    Task<string> CreateBeanAsync(FixtureBean value);
    Task<string> CreateBagAsync(FixtureBag value, FixtureIds ids);
    Task<string> CreateEquipmentAsync(FixtureEquipment value);
    Task<string> CreatePersonAsync(FixturePerson value);
    Task<string> CreateDrinkAsync(FixtureDrink value, FixtureIds ids);
    Task ApplyPreferencesAsync(FixtureDefinition fixture, FixtureIds ids);
    // Must create a fresh domain scope and read persisted values, not create responses.
    Task<FixtureReadback> ReadAsync(FixtureIds ids);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(FixtureDefinition))]
[JsonSerializable(typeof(FixtureReadback))]
[JsonSerializable(typeof(ProvisioningReceipt))]
[JsonSerializable(typeof(FixtureSelection))]
public partial class FixtureJson : JsonSerializerContext;
