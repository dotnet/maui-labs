using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BaristaComparison;

public static class FixtureValidation
{
    public static string ExpectedInputHash(int count) => count switch
    {
        100 => "22e04bcfc08a2e0975416a58e34e03879a618ed20ee5352038313a89f2bf94eb",
        1000 => "58643ebd288f83b11259226b711bd0062d7b249ef369d18c39c2950b238aa115",
        _ => throw new ArgumentOutOfRangeException(nameof(count), "Only approved v2 counts are supported.")
    };

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static FixtureDefinition Parse(byte[] bytes, int count)
    {
        if (Hash(bytes) != ExpectedInputHash(count))
            throw new InvalidDataException("Input bytes do not match the approved fixture.");
        var value = JsonSerializer.Deserialize(bytes, FixtureJson.Default.FixtureDefinition)
            ?? throw new InvalidDataException("Fixture is null.");
        Validate(value);
        return value;
    }

    public static DateTime Utc(string value) => DateTime.ParseExact(
        value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static DateTime CalendarDate(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);

    public static string FixtureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Local)
            throw new InvalidDataException("A local timestamp cannot be interpreted as fixture UTC.");
        return DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    public static void Validate(FixtureDefinition f)
    {
        Require(f.Version == 2 && f.Name == $"barista-perf-v2-{f.DrinkCount}", "Version/name");
        _ = ExpectedInputHash(f.DrinkCount);
        Require(f.Beans.Length == 5 && f.Bags.Length == 5 && f.Equipment.Length == 4 &&
            f.People.Length == 2 && f.Drinks.Length == f.DrinkCount, "Entity counts");
        Require(f.Preferences == new FixturePreferences("All", "Celsius", "Light", "Auto"), "Preferences");
        Require(f.ReferenceTimeUtc == "2026-09-01T12:00:00Z", "Reference time");
        var beans = Keys(f.Beans.Select(x => x.Key));
        var bags = Keys(f.Bags.Select(x => x.Key));
        var equipment = Keys(f.Equipment.Select(x => x.Key));
        var people = Keys(f.People.Select(x => x.Key));
        _ = Keys(f.Drinks.Select(x => x.Key));
        foreach (var bean in f.Beans)
            Require(!string.IsNullOrWhiteSpace(bean.Name) && bean.Name.Length <= 100, "Bean name");
        foreach (var bag in f.Bags)
        {
            Require(beans.Contains(bag.BeanKey) && !bag.IsComplete, "Bag reference/state");
            _ = CalendarDate(bag.RoastDate);
        }
        foreach (var item in f.Equipment)
            Require(item.Type is "Machine" or "Grinder", "Equipment type");
        DateTime? previous = null;
        foreach (var drink in f.Drinks)
        {
            var stamp = Utc(drink.TimestampUtc);
            Require(previous is null || stamp > previous, "Oldest-first drink order");
            previous = stamp;
            Require(bags.Contains(drink.BagKey) && equipment.Contains(drink.MachineKey) &&
                equipment.Contains(drink.GrinderKey) && people.Contains(drink.MadeByKey) &&
                people.Contains(drink.MadeForKey), "Drink reference");
            Require(f.Equipment.Single(x => x.Key == drink.MachineKey).Type == "Machine" &&
                f.Equipment.Single(x => x.Key == drink.GrinderKey).Type == "Grinder", "Equipment role");
            Require(drink.AccessoryKeys.Length == 0 && drink.PreinfusionTime is null, "V2 null/accessory contract");
            Require(drink.BrewMethod == "Espresso" && drink.DrinkType == "Espresso", "Brew/drink type");
            Require(drink.Rating is >= 0 and <= 4 && drink.DoseIn > 0 && drink.ExpectedOutput > 0 &&
                drink.ExpectedTime > 0, "Drink values");
        }
        Require(f.Drinks[^1].TimestampUtc == f.ReferenceTimeUtc &&
            f.Drinks.Count(x => x.Rating == 0) == f.DrinkCount / 5, "Latest time/rating zero");
    }

    public static void AssertReadback(FixtureDefinition f, FixtureReadback actual)
    {
        var newest = f.Drinks[^1];
        var expected = new FixtureReadback(
            f.Beans, f.Bags, f.Equipment, f.People, f.Drinks,
            new("Celsius", "Light", new(StringComparer.Ordinal)
            {
                ["dose"] = "Auto", ["grind"] = "Auto", ["output"] = "Auto", ["time"] = "Auto"
            }),
            new(newest.DrinkType, f.Bags.Single(x => x.Key == newest.BagKey).BeanKey,
                newest.BagKey, newest.MachineKey, newest.GrinderKey, newest.AccessoryKeys,
                newest.MadeByKey, newest.MadeForKey, newest.DoseIn, newest.GrindMicrons,
                newest.ExpectedTime, newest.ExpectedOutput, newest.PreinfusionTime),
            new(5, 5, 4, 2, f.DrinkCount), f.DrinkCount / 5,
            f.Drinks.Reverse().Select(x => x.Key).ToArray(),
            f.Drinks.Reverse().Take(100).Select(x => x.Key).ToArray());
        var expectedBytes = Canonical(expected);
        var actualBytes = Canonical(actual);
        Require(expectedBytes.AsSpan().SequenceEqual(actualBytes),
            $"Persisted semantic mismatch: expected {Hash(expectedBytes)}, actual {Hash(actualBytes)}");
    }

    public static byte[] Canonical(FixtureReadback value)
    {
        var ordered = value with
        {
            Beans = value.Beans.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Bags = value.Bags.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Equipment = value.Equipment.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            People = value.People.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Drinks = value.Drinks.OrderBy(x => Utc(x.TimestampUtc)).ThenBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x with { AccessoryKeys = x.AccessoryKeys.Order(StringComparer.Ordinal).ToArray() }).ToArray(),
            Recent = value.Recent with { AccessoryKeys = value.Recent.AccessoryKeys.Order(StringComparer.Ordinal).ToArray() }
        };
        var element = JsonSerializer.SerializeToElement(ordered, FixtureJson.Default.FixtureReadback);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, element);
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var member in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(member.Name);
                    WriteCanonical(writer, member.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static HashSet<string> Keys(IEnumerable<string> values)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in values)
            Require(!string.IsNullOrWhiteSpace(key) && keys.Add(key), "Duplicate/empty logical key");
        return keys;
    }

    public static void Require(bool condition, string reason)
    {
        if (!condition)
            throw new InvalidDataException(reason);
    }
}
