using System.Text;
using System.Text.Json;
using BaristaComparison;

namespace BaristaComparison.Tests;

public sealed class FixtureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "barista-fixture-tests-" + Guid.NewGuid().ToString("N"));
    private static byte[] Input(int count) => File.ReadAllBytes(Path.Combine(
        Environment.GetEnvironmentVariable("BARISTA_FIXTURE_INPUTS")
            ?? throw new InvalidOperationException("Set BARISTA_FIXTURE_INPUTS to the exact approved v2 input directory."),
        $"barista-perf-v2-{count}.json"));
    private static FixtureDefinition Fixture(int count = 100) => FixtureValidation.Parse(Input(count), count);
    private static FixtureSelection Selection(int count = 100) => new($"fixture-v2-{count}-tests", count, "provision");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void Parse_ApprovedBytes_PreservesNullZeroAndOrder(int count)
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        var fixture = Fixture(count);
        Assert.Equal(count, fixture.Drinks.Length);
        Assert.All(fixture.Drinks, x => Assert.Null(x.PreinfusionTime));
        Assert.Equal(count / 5, fixture.Drinks.Count(x => x.Rating == 0));
        Assert.Equal("drink-0001", fixture.Drinks[^1].Key);
    }

    [Fact]
    public void Parse_ChangedBytes_RefusesBeforeAnyStorage()
    {
        Assert.Throws<InvalidDataException>(() => FixtureValidation.Parse(Encoding.UTF8.GetBytes("{}"), 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => FixtureValidation.ExpectedInputHash(99));
    }

    [Theory]
    [InlineData("{\"unexpected\":1}")]
    [InlineData("null")]
    public void Deserialize_InvalidShape_CannotBeAccepted(string json)
    {
        if (json == "null")
            Assert.Null(JsonSerializer.Deserialize(json, FixtureJson.Default.FixtureDefinition));
        else
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, FixtureJson.Default.FixtureDefinition));
    }

    [Fact]
    public void Validate_DuplicateKeyOrMissingReference_Refuses()
    {
        var fixture = Fixture();
        Assert.Throws<InvalidDataException>(() => FixtureValidation.Validate(fixture with
        {
            Beans = [fixture.Beans[0], fixture.Beans[0], .. fixture.Beans.Skip(2)]
        }));
        Assert.Throws<InvalidDataException>(() => FixtureValidation.Validate(fixture with
        {
            Bags = [fixture.Bags[0] with { BeanKey = "missing" }, .. fixture.Bags.Skip(1)]
        }));
        Assert.Throws<InvalidDataException>(() => FixtureValidation.Validate(fixture with
        {
            Drinks = [fixture.Drinks[0] with { PreinfusionTime = 0 }, .. fixture.Drinks.Skip(1)]
        }));
    }

    [Theory]
    [InlineData("../default")]
    [InlineData("/data/default")]
    [InlineData("fixture-v2-100-../default")]
    [InlineData("review")]
    [InlineData("fixture-v2-1000-tests")]
    [InlineData("fixture-v2-100-x\n")]
    public void Namespace_InvalidSegmentOrCount_Refuses(string name) =>
        Assert.Throws<InvalidDataException>(() => FixtureStore.NamespacePath(_root, new(name, 100, "provision")));

    [Fact]
    public void Dates_UnspecifiedIsUtcWithoutLocalConversion()
    {
        var digits = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal("2026-09-01T12:00:00Z", FixtureValidation.FixtureUtc(digits));
        Assert.Equal(DateTimeKind.Unspecified, FixtureValidation.CalendarDate("2026-08-15").Kind);
        Assert.Throws<InvalidDataException>(() => FixtureValidation.FixtureUtc(DateTime.SpecifyKind(digits, DateTimeKind.Local)));
    }

    [Fact]
    public void Canonical_OrderAndDecimalScaleAreStable_ButNullIsNotZero()
    {
        var value = Expected(Fixture());
        var reordered = value with
        {
            Beans = value.Beans.Reverse().ToArray(),
            Drinks = value.Drinks.Reverse().Select(x => x with { DoseIn = x.DoseIn * 1.00m }).ToArray()
        };
        Assert.Equal(FixtureValidation.Canonical(value), FixtureValidation.Canonical(reordered));
        var changed = value with { Drinks = [value.Drinks[0] with { PreinfusionTime = 0 }, .. value.Drinks.Skip(1)] };
        Assert.NotEqual(FixtureValidation.Hash(FixtureValidation.Canonical(value)),
            FixtureValidation.Hash(FixtureValidation.Canonical(changed)));
        Assert.Throws<InvalidDataException>(() => FixtureValidation.AssertReadback(Fixture(), changed));
    }

    [Fact]
    public async Task Provision_FreshNamespace_PreservesDefaultAndSecondNamespace()
    {
        Directory.CreateDirectory(_root);
        var defaultFile = Path.Combine(_root, "barista_notes.db");
        File.WriteAllText(defaultFile, "untouched review database");
        var defaultPreferences = Path.Combine(_root, "default-preferences.xml");
        File.WriteAllText(defaultPreferences, "untouched preferences");
        var fixture = Fixture();
        var store = new FixtureStore(_root, Selection());
        var adapter = new Adapter(Expected(fixture));
        await store.ExecuteAsync(fixture, adapter);
        Assert.Equal(116, adapter.Creates);
        Assert.Equal(1, adapter.PreferenceWrites);
        Assert.Equal("complete", store.Receipt.State);
        Assert.Equal(100, store.Receipt.Ids.Drinks.Count);
        Assert.Equal("untouched review database", File.ReadAllText(defaultFile));
        Assert.Equal("untouched preferences", File.ReadAllText(defaultPreferences));
        Assert.NotEqual(store.DatabasePath, defaultFile);
        Assert.NotEqual(store.DatabasePath, FixtureStore.NamespacePath(_root, Selection(1000)));
    }

    [Fact]
    public async Task ExistingComplete_ValidatesWithoutWrites_AndRefusesChangedValues()
    {
        var fixture = Fixture();
        var store = new FixtureStore(_root, Selection());
        var adapter = new Adapter(Expected(fixture));
        await store.ExecuteAsync(fixture, adapter);
        var receiptFile = Path.Combine(store.DirectoryPath, "receipt.json");
        var receipt = File.ReadAllBytes(receiptFile);
        var existing = new FixtureStore(_root, Selection() with { Mode = "validate" });
        var readOnly = new Adapter(Expected(fixture));
        await existing.ExecuteAsync(fixture, readOnly);
        Assert.Equal(0, readOnly.Creates);
        Assert.Equal(0, readOnly.PreferenceWrites);
        Assert.Equal(receipt, File.ReadAllBytes(receiptFile));
        var bad = new Adapter(Expected(fixture) with { RatingZeroCount = 19 });
        await Assert.ThrowsAsync<InvalidDataException>(() => existing.ExecuteAsync(fixture, bad));
        Assert.Equal(receipt, File.ReadAllBytes(receiptFile));
    }

    [Fact]
    public async Task Failure_RetainsIdsAndPendingOperation_NoRetryOrRepair()
    {
        var fixture = Fixture();
        var store = new FixtureStore(_root, Selection());
        var adapter = new Adapter(Expected(fixture)) { FailAt = 4 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(fixture, adapter));
        Assert.Equal("failed", store.Receipt.State);
        Assert.Equal(3, store.Receipt.Ids.Beans.Count);
        Assert.Equal("bean:bean-04", store.Receipt.PendingOperation);
        Assert.Contains("deliberate domain failure", store.Receipt.Failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(fixture, adapter));
        Assert.Equal(4, adapter.Creates);
        Assert.Throws<InvalidDataException>(() => new FixtureStore(_root, Selection()));
    }

    [Fact]
    public void ExistingIncompleteOrUnowned_Refuses()
    {
        _ = new FixtureStore(_root, Selection());
        Assert.Throws<InvalidDataException>(() => new FixtureStore(_root, Selection()));
        var second = Selection(1000);
        Directory.CreateDirectory(FixtureStore.NamespacePath(_root, second));
        Assert.Throws<InvalidDataException>(() => new FixtureStore(_root, second));
    }

    [Fact]
    public async Task DuplicateReturnedId_FailsWithoutRetryOrLostFirstId()
    {
        var fixture = Fixture();
        var store = new FixtureStore(_root, Selection());
        var adapter = new Adapter(Expected(fixture)) { DuplicateAt = 18 };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ExecuteAsync(fixture, adapter));
        Assert.Equal(18, adapter.Creates);
        Assert.Single(store.Receipt.Ids.Drinks);
        Assert.Equal("17", store.Receipt.Ids.Drinks["drink-0100"]);
        Assert.Equal("drink:drink-0099", store.Receipt.PendingOperation);
        Assert.Equal("failed", store.Receipt.State);
        Assert.Equal(0, adapter.PreferenceWrites);
        Assert.Null(store.Receipt.SemanticSha256);
    }

    [Fact]
    public async Task ExistingReceiptWithWrongInputHash_RefusesWithoutWrites()
    {
        var fixture = Fixture();
        var store = new FixtureStore(_root, Selection());
        await store.ExecuteAsync(fixture, new Adapter(Expected(fixture)));
        var path = Path.Combine(store.DirectoryPath, "receipt.json");
        var changed = File.ReadAllText(path).Replace(store.Receipt.InputSha256, new string('0', 64));
        File.WriteAllText(path, changed);
        Assert.Throws<InvalidDataException>(() => new FixtureStore(_root, Selection()));
        Assert.Equal(changed, File.ReadAllText(path));
    }

    private static FixtureReadback Expected(FixtureDefinition f)
    {
        var n = f.Drinks[^1];
        return new(f.Beans, f.Bags, f.Equipment, f.People, f.Drinks,
            new("Celsius", "Light", new() { ["dose"] = "Auto", ["grind"] = "Auto", ["output"] = "Auto", ["time"] = "Auto" }),
            new(n.DrinkType, f.Bags.Single(x => x.Key == n.BagKey).BeanKey, n.BagKey, n.MachineKey,
                n.GrinderKey, n.AccessoryKeys, n.MadeByKey, n.MadeForKey, n.DoseIn, n.GrindMicrons,
                n.ExpectedTime, n.ExpectedOutput, n.PreinfusionTime),
            new(5, 5, 4, 2, f.DrinkCount), f.DrinkCount / 5,
            f.Drinks.Reverse().Select(x => x.Key).ToArray(), f.Drinks.Reverse().Take(100).Select(x => x.Key).ToArray());
    }

    private sealed class Adapter(FixtureReadback result) : IFixtureAdapter
    {
        public int Creates { get; private set; }
        public int PreferenceWrites { get; private set; }
        public int FailAt { get; init; }
        public int DuplicateAt { get; init; }
        private Task<string> Create()
        {
            Creates++;
            if (Creates == FailAt)
                throw new InvalidOperationException("deliberate domain failure");
            return Task.FromResult((Creates == DuplicateAt ? Creates - 1 : Creates)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        public Task<string> CreateBeanAsync(FixtureBean value) => Create();
        public Task<string> CreateBagAsync(FixtureBag value, FixtureIds ids) => Create();
        public Task<string> CreateEquipmentAsync(FixtureEquipment value) => Create();
        public Task<string> CreatePersonAsync(FixturePerson value) => Create();
        public Task<string> CreateDrinkAsync(FixtureDrink value, FixtureIds ids) => Create();
        public Task ApplyPreferencesAsync(FixtureDefinition fixture, FixtureIds ids)
        {
            PreferenceWrites++;
            return Task.CompletedTask;
        }
        public Task<FixtureReadback> ReadAsync(FixtureIds ids) => Task.FromResult(result);
    }
}
