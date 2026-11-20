#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BaristaComparison;
using CometBaristaNotes.Comparison;
using CometBaristaNotes.Data;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometSamples.BaristaNotes;
using CometSamples.BaristaNotes.Styles;
using Comet.Tests.VoiceDefects;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaFixtures;

[Collection(VoiceIntegrationCollection.Name)]
public sealed class CometFixtureTests
{
    static string NewDirectory() => IOPath.Combine(
        Environment.CurrentDirectory, "artifacts", "comet-fixture-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("../default", 100, "provision")]
    [InlineData("fixture-v2-100-Upper", 100, "provision")]
    [InlineData("fixture-v2-100-good", 1000, "provision")]
    [InlineData("fixture-v2-100-good", 100, "retry")]
    [InlineData("fixture-v2-100-good", 100, "validate")]
    [InlineData("fixture-v2-1000-good", 1000, "run")]
    public void InvalidOrMissingSelection_RefusesBeforeAnyWrite(string name, int count, string mode)
    {
        var root = NewDirectory();
        Assert.ThrowsAny<Exception>(() => CometFixtureSession.Preflight(
            root, new FixtureSelection(name, count, mode), _ => false));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void OrphanedPreferences_RefuseBeforeNamespaceReservation()
    {
        var root = NewDirectory();
        Assert.Throws<InvalidDataException>(() => CometFixtureSession.Preflight(
            root, new FixtureSelection("fixture-v2-100-orphan", 100, "provision"),
            name => name == "barista_comparison_fixture-v2-100-orphan"));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("provisioning")]
    [InlineData("failed")]
    [InlineData("complete")]
    public void IncompleteFailedOrMissingDatabase_RefuseWithoutChangingEvidence(string state)
    {
        var root = NewDirectory();
        var selection = new FixtureSelection("fixture-v2-100-refuse", 100, "provision");
        var store = new FixtureStore(root, selection);
        store.Receipt.State = state;
        store.Receipt.SemanticSha256 = new string('a', 64);
        var path = IOPath.Combine(store.DirectoryPath, "receipt.json");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(store.Receipt, FixtureJson.Default.ProvisioningReceipt));
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => CometFixtureSession.Preflight(root, selection, _ => false));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(store.DatabasePath));
    }

    [Fact]
    public async Task ActivityReplacement_WaitsForCleanupThenReopensSelectedData()
    {
        using (var anchor = new InMemoryDataStore())
        {
            BaristaServiceLocator.Initialize(anchor, new DataChangeNotifier());
            BaristaServiceLocator.Reset(anchor);
        }
        var root = NewDirectory();
        var storage = new BaristaAppStorage(IOPath.Combine(root, "default.db"), new RejectPreferences());
        storage.ConfigureComparisonDatabase(IOPath.Combine(root, "fixture.db"));
        var first = new BaristaServices(storage);
        await first.BeanService.CreateBeanAsync(new CreateBeanDto { Name = "Retained through recreation" });
        first.ThemePreferences.Save(CoffeeThemeMode.Light);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownership = new BaristaHostOwnership();
        var oldActivity = new object();
        var newActivity = new object();
        var releaseCount = 0;
        await ownership.AcquireAsync(oldActivity, async () =>
        {
            releaseCount++;
            entered.TrySetResult(true);
            await complete.Task;
            first.Dispose();
        });
        BaristaServices? second = null;
        var replacement = ownership.AcquireAsync(newActivity, () =>
        {
            second?.Dispose();
            return Task.CompletedTask;
        });
        await entered.Task;
        Assert.False(replacement.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => new BaristaServices(storage));
        complete.SetResult(true);
        await replacement;
        second = new BaristaServices(storage);
        try
        {
            Assert.Equal("Retained through recreation", Assert.Single(await second.BeanService.GetAllActiveBeansAsync()).Name);
            Assert.Equal(CoffeeThemeMode.Light, second.ThemePreferences.Load());
            await ownership.ReleaseAsync(oldActivity);
            Assert.True(ownership.IsOwner(newActivity));
            Assert.Single(await second.BeanService.GetAllActiveBeansAsync());
            Assert.Equal(1, releaseCount);
        }
        finally
        {
            await ownership.ReleaseAsync(newActivity);
        }
        Assert.False(ownership.IsOwner(newActivity));
    }

    [Fact]
    public async Task FailedTeardown_DoesNotGrantReplacementOwnership()
    {
        var ownership = new BaristaHostOwnership();
        var oldActivity = new object();
        var newActivity = new object();
        await ownership.AcquireAsync(oldActivity,
            () => Task.FromException(new IOException("Test cleanup failure")));
        await Assert.ThrowsAsync<IOException>(() => ownership.AcquireAsync(newActivity, () => Task.CompletedTask));
        Assert.True(ownership.IsOwner(oldActivity));
        Assert.False(ownership.IsOwner(newActivity));
    }

    [Fact]
    public void DuplicateProcessSelection_DoesNotReserveAnotherNamespace()
    {
        var root = NewDirectory();
        var storage = new BaristaAppStorage(IOPath.Combine(root, "default.db"), new RejectPreferences());
        _ = new CometFixtureSession(root, new FixtureSelection("fixture-v2-100-first", 100, "provision"), storage, _ => false);
        var second = new FixtureSelection("fixture-v2-100-second", 100, "provision");
        Assert.Throws<InvalidOperationException>(() => new CometFixtureSession(root, second, storage, _ => false));
        Assert.False(Directory.Exists(FixtureStore.NamespacePath(root, second)));
        Assert.False(File.Exists(IOPath.Combine(root, "default.db")));
    }

    [Fact]
    public void ExportChunks_ReassembleExactBytesWithSourceGeneratedMetadata()
    {
        var root = NewDirectory();
        var selected = new FixtureSelection("fixture-v2-100-export", 100, "run");
        var token = Guid.NewGuid().ToString("N");
        var directory = IOPath.Combine(root, "barista-comparison-control", "exports");
        Directory.CreateDirectory(directory);
        var bytes = Enumerable.Range(0, 20000).Select(index => (byte)(index % 251)).ToArray();
        File.WriteAllBytes(IOPath.Combine(directory, selected.Namespace + "-" + token + ".json"), bytes);
        var first = CometFixtureExports.Read(root, selected, token, 0, 16384);
        var last = CometFixtureExports.Read(root, selected, token, first.Length, 16384);
        Assert.Equal(bytes, Convert.FromBase64String(first.Base64).Concat(Convert.FromBase64String(last.Base64)).ToArray());
        Assert.Equal(FixtureValidation.Hash(bytes), first.Sha256);
        Assert.Equal(first.Sha256, last.Sha256);
        Assert.Equal(bytes.Length, last.TotalBytes);
        var json = JsonSerializer.SerializeToUtf8Bytes(last, CometFixtureStatusJson.Default.CometFixtureChunk);
        Assert.Equal(last, JsonSerializer.Deserialize(json, CometFixtureStatusJson.Default.CometFixtureChunk));
        Assert.Throws<InvalidDataException>(() => CometFixtureExports.Read(root, selected, token, bytes.Length, 1));
    }

    [Theory]
    [InlineData("../default", 0, 1)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 0, 1)]
    [InlineData("00000000000000000000000000000000", -1, 1)]
    [InlineData("00000000000000000000000000000000", 0, 0)]
    [InlineData("00000000000000000000000000000000", 0, 16385)]
    public void InvalidExportRequest_RejectsBeforeFileAccess(string token, int offset, int length)
    {
        var root = NewDirectory();
        Assert.Throws<InvalidDataException>(() => CometFixtureExports.Read(
            root, new FixtureSelection("fixture-v2-100-export", 100, "run"), token, offset, length));
        Assert.False(Directory.Exists(root));
    }

    internal sealed class RejectPreferences : IPreferencesStore
    {
        public string? Get(string key, string? value) => throw new InvalidOperationException("Default theme was read.");
        public int Get(string key, int value) => throw new InvalidOperationException("Default preferences were read.");
        public double Get(string key, double value) => throw new InvalidOperationException("Default preferences were read.");
        public void Set(string key, string value) => throw new InvalidOperationException("Default theme was written.");
        public void Set(string key, int value) => throw new InvalidOperationException("Default preferences were written.");
        public void Set(string key, double value) => throw new InvalidOperationException("Default preferences were written.");
        public void Remove(string key) => throw new InvalidOperationException("Default preferences were removed.");
    }
}

// Invoked by the separate HostProof executable, once per fresh process.
public static class CometFixtureHostProof
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4)
            throw new ArgumentException("Expected: input-directory app-data namespace provision|validate|run.");
        var selection = new FixtureSelection(args[2],
            args[2].StartsWith("fixture-v2-1000-", StringComparison.Ordinal) ? 1000 : 100, args[3]);
        var defaultPath = IOPath.Combine(args[1], "default", "baristanotes.db");
        if (!File.Exists(defaultPath))
        {
            using var normal = new SqliteDataStore(defaultPath);
            Assert.Single(normal.Shots);
        }
        var defaultBytes = File.ReadAllBytes(defaultPath);
        var storage = new BaristaAppStorage(defaultPath, new CometFixtureTests.RejectPreferences());
        var session = new CometFixtureSession(args[1], selection, storage, _ => false);
        var receiptPath = IOPath.Combine(session.Store.DirectoryPath, "receipt.json");
        var readbackPath = IOPath.Combine(session.Store.DirectoryPath, "readback.json");
        var beforeReceipt = File.ReadAllBytes(receiptPath);
        var beforeReadback = File.Exists(readbackPath) ? File.ReadAllBytes(readbackPath) : null;
        var beforeDatabase = File.Exists(session.Store.DatabasePath) ? File.ReadAllBytes(session.Store.DatabasePath) : null;
        var loadCount = 0;
        byte[] Load(int count)
        {
            loadCount++;
            return File.ReadAllBytes(IOPath.Combine(args[0], $"barista-perf-v2-{count}.json"));
        }

        using (var services = new BaristaServices(storage))
        {
            var adapter = new CometFixtureAdapter(session.Store.DatabasePath, services);
            var actual = await session.StartAsync(adapter, Load);
            if (selection.Mode == "run")
            {
                Assert.Null(actual);
                Assert.Equal(0, loadCount);
                Assert.Equal(selection.DrinkCount, (await services.ShotService.GetShotHistoryAsync(0, 100)).TotalCount);
                Assert.Equal(CoffeeThemeMode.Light, services.ThemePreferences.Load());
            }
            else
            {
                Assert.Equal(1, loadCount);
                Assert.NotNull(actual);
                Assert.Equal(selection.DrinkCount, actual.Counts.Drinks);
                Assert.Equal(selection.DrinkCount / 5, actual.RatingZeroCount);
                Assert.Equal(100, actual.Latest100.Length);
                Assert.All(actual.Drinks, drink => Assert.Null(drink.PreinfusionTime));
                Assert.All(actual.Drinks, drink => Assert.Empty(drink.AccessoryKeys));
                Assert.Equal(actual.HistoryNewestFirst.Take(100), actual.Latest100);
                Assert.Equal("drink-0001", actual.HistoryNewestFirst[0]);
                Assert.Equal(5, actual.Counts.Beans);
                Assert.Equal(5, actual.Counts.Bags);
                Assert.Equal(4, actual.Counts.Equipment);
                Assert.Equal(2, actual.Counts.People);
                Assert.Equal(selection.DrinkCount, session.Store.Receipt.Ids.Drinks.Values.Distinct().Count());
                foreach (var ids in new[] { session.Store.Receipt.Ids.Beans, session.Store.Receipt.Ids.Bags,
                    session.Store.Receipt.Ids.Equipment, session.Store.Receipt.Ids.People, session.Store.Receipt.Ids.Drinks })
                    Assert.All(ids.Values, value => Assert.True(int.Parse(value) > 0));
                var orphaned = new FixtureIds { Drinks = new Dictionary<string, string>(session.Store.Receipt.Ids.Drinks) };
                await Assert.ThrowsAsync<InvalidDataException>(() => adapter.ReadAsync(orphaned));
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(adapter, Load));
                var evidence = IOPath.Combine(args[1], $"{selection.Namespace}-{selection.Mode}-actual.json");
                File.WriteAllBytes(evidence, FixtureValidation.Canonical(actual));
            }
        }
        Assert.Equal(defaultBytes, File.ReadAllBytes(defaultPath));
        if (!session.Store.IsNew)
        {
            Assert.Equal(beforeReceipt, File.ReadAllBytes(receiptPath));
            Assert.Equal(beforeReadback, File.ReadAllBytes(readbackPath));
            Assert.Equal(beforeDatabase, File.ReadAllBytes(session.Store.DatabasePath));
        }
        Console.WriteLine($"PASS {selection.Namespace} {selection.Mode}; input loads={loadCount}; default data unchanged.");
    }
}
