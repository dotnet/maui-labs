#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BaristaComparison;
using CometBaristaNotes.Comparison;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometSamples.BaristaNotes;
using Comet.Tests.VoiceDefects;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaFixtures;

[Collection(VoiceIntegrationCollection.Name)]
public sealed class CometFixtureCorrectionTests
{
    internal const string UnsupportedRanges = "{\"SchemaVersion\":999,\"Modes\":{},\"Overrides\":[]}";

    internal static string NewDirectory() => IOPath.Combine(
        AppContext.BaseDirectory, "artifacts", "comet-fixture-corrections", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{")]
    [InlineData(UnsupportedRanges)]
    public async Task InvalidPersistedRanges_RefuseReadbackWithoutRewritingTheSettings(string badJson)
    {
        var database = IOPath.Combine(NewDirectory(), "barista_notes.db");
        using (var store = new SqliteDataStore(database, seedOnFirstRun: false))
            new PreferencesService(store.CreatePreferencesStore()).SetDrinkValueRangeSettingsJson(badJson);
        var bytes = File.ReadAllBytes(database);
        var error = await Record.ExceptionAsync(() => new CometFixtureAdapter(database).ReadAsync(new FixtureIds()));
        Assert.Equal(bytes, File.ReadAllBytes(database));
        using (var store = new SqliteDataStore(database, seedOnFirstRun: false))
        {
            var preferences = new PreferencesService(store.CreatePreferencesStore());
            Assert.Equal(badJson, preferences.GetDrinkValueRangeSettingsJson());
            var ranges = new InMemoryDrinkValueRangeService(preferences);
            Assert.NotNull(ranges.GetSettings().LoadWarning);
            Assert.Equal(ValueRangeMode.Auto, ranges.GetMode(DrinkValueMetric.DoseIn));
        }
        Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public async Task NonDefaultAncestorAlias_MatchesItsWriterButNotAnotherDatabase()
    {
        ResetLocator();
        var root = NewDirectory();
        var physical = IOPath.Combine(root, "physical");
        Directory.CreateDirectory(physical);
        var alias = IOPath.Combine(root, "alias");
        Directory.CreateSymbolicLink(alias, physical);
        var defaultPath = IOPath.Combine(physical, "default.db");
        var storage = new BaristaAppStorage(defaultPath, new CometFixtureTests.RejectPreferences());
        storage.ConfigureComparisonDatabase(IOPath.Combine(alias, "selected.db"));
        using var writer = new BaristaServices(storage);
        var selected = Assert.IsType<SqliteDataStore>(writer.Store);
        Assert.Equal(IOPath.Combine(physical, "selected.db"), selected.DatabasePath);
        Assert.NotEqual(IOPath.Combine(alias, "selected.db"), selected.DatabasePath);
        var adapter = new CometFixtureAdapter(IOPath.Combine(alias, "selected.db"), writer);
        var id = await adapter.CreateBeanAsync(new FixtureBean("bean", "Alias bean", null, null, null));
        var ids = new FixtureIds();
        ids.Beans.Add("bean", id);
        Assert.Equal("Alias bean", Assert.Single((await adapter.ReadAsync(ids)).Beans).Name);

        var different = new CometFixtureAdapter(IOPath.Combine(physical, "other", "selected.db"), writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            different.CreateBeanAsync(new FixtureBean("wrong", "Wrong store", null, null, null)));
        Assert.Single(writer.Store.Beans);
        Assert.False(File.Exists(IOPath.Combine(physical, "other", "selected.db")));
        Assert.Throws<InvalidOperationException>(() => new BaristaServices(storage));
        Assert.False(File.Exists(defaultPath));
    }

    [Fact]
    public void DefaultDatabaseThroughRealAncestorSymlink_IsStillRejected()
    {
        var root = NewDirectory();
        var physical = IOPath.Combine(root, "physical");
        Directory.CreateDirectory(physical);
        var alias = IOPath.Combine(root, "alias");
        Directory.CreateSymbolicLink(alias, physical);
        var defaultPath = IOPath.Combine(physical, "baristanotes.db");
        var storage = new BaristaAppStorage(defaultPath, new CometFixtureTests.RejectPreferences());
        Assert.Throws<ArgumentException>(() =>
            storage.ConfigureComparisonDatabase(IOPath.Combine(alias, "baristanotes.db")));
        Assert.False(File.Exists(defaultPath));
    }

    internal static void ResetLocator()
    {
        using var anchor = new InMemoryDataStore();
        BaristaServiceLocator.Initialize(anchor, new DataChangeNotifier());
        BaristaServiceLocator.Reset(anchor);
    }
}

// The existing HostProof executable runs these cases with the exact approved input bytes.
public static class CometFixtureCorrectionProof
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4 || args[3] is not
            ("check-malformed-ranges" or "check-unsupported-ranges" or "check-aliased-namespace"))
            throw new ArgumentException("Expected input-directory app-data namespace check-*.");

        CometFixtureCorrectionTests.ResetLocator();
        var selection = new FixtureSelection(args[2], 100, "provision");
        _ = FixtureStore.NamespacePath(args[1], selection);
        var input = File.ReadAllBytes(IOPath.Combine(args[0], "barista-perf-v2-100.json"));
        var fixture = FixtureValidation.Parse(input, 100);
        var physical = IOPath.Combine(args[1], "physical");
        Directory.CreateDirectory(physical);
        var appData = physical;
        var checkAlias = args[3] == "check-aliased-namespace";
        if (checkAlias)
        {
            appData = IOPath.Combine(args[1], "alias");
            Directory.CreateSymbolicLink(appData, physical);
        }
        var defaultPath = IOPath.Combine(physical, "default.db");
        using (var normal = new SqliteDataStore(defaultPath))
            Assert.Single(normal.Shots);
        var defaultBytes = File.ReadAllBytes(defaultPath);
        var storage = new BaristaAppStorage(defaultPath, new CometFixtureTests.RejectPreferences());
        var session = new CometFixtureSession(appData, selection, storage, _ => false);
        using (var writer = new BaristaServices(storage))
        {
            if (checkAlias)
            {
                var actualPath = Assert.IsType<SqliteDataStore>(writer.Store).DatabasePath;
                Assert.NotEqual(session.Store.DatabasePath, actualPath);
                Console.WriteLine($"Namespace path: {session.Store.DatabasePath}; selected writer path: {actualPath}");
            }
            var actual = await session.StartAsync(new CometFixtureAdapter(session.Store.DatabasePath, writer), _ => input);
            Assert.NotNull(actual);
            Assert.Equal(100, actual.Counts.Drinks);
        }
        Assert.Equal("complete", session.Store.Receipt.State);
        var receiptPath = IOPath.Combine(session.Store.DirectoryPath, "receipt.json");
        var readbackPath = IOPath.Combine(session.Store.DirectoryPath, "readback.json");
        var receiptBytes = File.ReadAllBytes(receiptPath);
        var readbackBytes = File.ReadAllBytes(readbackPath);
        var badJson = args[3] == "check-malformed-ranges" ? "{" : CometFixtureCorrectionTests.UnsupportedRanges;
        if (!checkAlias)
        {
            using var persisted = new SqliteDataStore(session.Store.DatabasePath, seedOnFirstRun: false);
            var preferences = new PreferencesService(persisted.CreatePreferencesStore());
            preferences.SetDrinkValueRangeSettingsJson(badJson);
            var ranges = new InMemoryDrinkValueRangeService(preferences);
            Assert.NotNull(ranges.GetSettings().LoadWarning);
            Assert.Equal(ValueRangeMode.Auto, ranges.GetMode(DrinkValueMetric.DoseIn));
            Console.WriteLine("Persisted range warning: " + ranges.GetSettings().LoadWarning);
        }
        var databaseBytes = File.ReadAllBytes(session.Store.DatabasePath);
        var complete = new FixtureStore(appData, selection with { Mode = "validate" });
        FixtureReadback? validated = null;
        var error = await Record.ExceptionAsync(async () =>
            validated = await complete.ValidateAsync(fixture, new CometFixtureAdapter(complete.DatabasePath)));
        Assert.Equal(databaseBytes, File.ReadAllBytes(complete.DatabasePath));
        Assert.Equal(receiptBytes, File.ReadAllBytes(receiptPath));
        Assert.Equal(readbackBytes, File.ReadAllBytes(readbackPath));
        Assert.Equal(defaultBytes, File.ReadAllBytes(defaultPath));
        Assert.Equal("complete", complete.Receipt.State);
        if (checkAlias)
        {
            Assert.Null(error);
            Assert.NotNull(validated);
            var direct = new FixtureStore(physical, selection with { Mode = "validate" });
            var directReadback = await direct.ValidateAsync(fixture, new CometFixtureAdapter(direct.DatabasePath));
            Assert.Equal(FixtureValidation.Canonical(validated), FixtureValidation.Canonical(directReadback));
            Assert.Equal(receiptBytes, File.ReadAllBytes(receiptPath));
            Assert.Equal(readbackBytes, File.ReadAllBytes(readbackPath));
        }
        else
        {
            using var persisted = new SqliteDataStore(complete.DatabasePath, seedOnFirstRun: false);
            Assert.Equal(badJson, new PreferencesService(persisted.CreatePreferencesStore()).GetDrinkValueRangeSettingsJson());
            if (validated is not null)
                Console.WriteLine($"INCORRECT ACCEPTANCE: actual={FixtureValidation.Hash(FixtureValidation.Canonical(validated))}; receipt={complete.Receipt.SemanticSha256}");
            Assert.IsType<InvalidDataException>(error);
            Console.WriteLine("Validation refused: " + error.Message);
        }
        Console.WriteLine($"PASS {args[3]}; input={FixtureValidation.Hash(input)}; complete receipt/readback and default data preserved.");
    }
}
