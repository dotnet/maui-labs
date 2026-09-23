#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometSamples.BaristaNotes;
using CometSamples.BaristaNotes.Styles;
using Comet.Tests.VoiceDefects;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaStorage;

[Collection(VoiceIntegrationCollection.Name)]
public sealed class BaristaComparisonStorageTests : IDisposable
{
    readonly string _directory = IOPath.Combine(
        Environment.CurrentDirectory, "artifacts", "barista-storage-tests", Guid.NewGuid().ToString("N"));

    public BaristaComparisonStorageTests()
    {
        Directory.CreateDirectory(_directory);
        // Legacy host tests can leave a non-exclusive locator binding after they finish.
        using var anchor = new InMemoryDataStore();
        BaristaServiceLocator.Initialize(anchor, new DataChangeNotifier());
        Assert.True(BaristaServiceLocator.Reset(anchor));
    }

    string DefaultPath => IOPath.Combine(_directory, "default", "baristanotes.db");
    string NamespacePath(string name) => IOPath.Combine(_directory, name, "baristanotes.db");

    BaristaAppStorage Storage(ThemeStore? theme = null) =>
        new(DefaultPath, theme ?? new ThemeStore { RejectAccess = true });

    [Fact]
    public void DefaultStorage_PreservesPathDemoSeedAndMauiThemeKeySemantics()
    {
        var theme = new ThemeStore();
        theme.Set(BaristaThemePreferences.Key, "Dark");
        var storage = Storage(theme);
        using (var services = new BaristaServices(storage))
        {
            var sqlite = Assert.IsType<SqliteDataStore>(services.Store);
            Assert.Equal(DefaultPath, sqlite.DatabasePath);
            Assert.Equal(2, sqlite.Beans.Count);
            Assert.Single(sqlite.Shots);
            Assert.Equal(CoffeeThemeMode.Dark, services.ThemePreferences.Load());
            services.ThemePreferences.Save(CoffeeThemeMode.Light);
            Assert.Equal("Light", theme.Get("baristanotes_theme", (string?)null));
            Assert.Null(sqlite.CreatePreferencesStore().Get(BaristaThemePreferences.Key, (string?)null));
            services.Preferences.SetLastDrinkType("Default drink");
        }

        using var reopened = new BaristaServices(storage);
        Assert.Single(reopened.Store.Shots);
        Assert.Equal("Default drink", reopened.Preferences.GetLastDrinkType());
        Assert.Equal(CoffeeThemeMode.Light, reopened.ThemePreferences.Load());
        Assert.All(theme.Keys, key => Assert.Equal("baristanotes_theme", key));
    }

    [Fact]
    public async Task TwoNamespaces_KeepRealDataAndPreferencesIndependentAcrossReopen()
    {
        var theme = new ThemeStore { RejectAccess = true };
        var first = Storage(theme);
        var second = Storage(theme);
        first.ConfigureComparisonDatabase(NamespacePath("one"));
        second.ConfigureComparisonDatabase(NamespacePath("two"));

        using (var services = new BaristaServices(first))
        {
            Assert.Empty(services.Store.Beans);
            Assert.Empty(services.Store.Bags);
            Assert.Empty(services.Store.Shots);
            Assert.Empty(services.Store.Equipment);
            Assert.Empty(services.Store.Profiles);
            Assert.Equal(CoffeeThemeMode.System, services.ThemePreferences.Load());
            await services.BeanService.CreateBeanAsync(new CreateBeanDto { Name = "Only one" });
            services.Preferences.SetLastDrinkType("One");
            services.Preferences.SetTemperatureUnit(TemperatureUnit.Celsius);
            services.ThemePreferences.Save(CoffeeThemeMode.Dark);
        }
        using (var services = new BaristaServices(second))
        {
            Assert.Empty(services.Store.Beans);
            Assert.Empty(services.Store.Shots);
            Assert.Null(services.Preferences.GetLastDrinkType());
            Assert.Equal(TemperatureUnit.Fahrenheit, services.Preferences.GetTemperatureUnit());
            Assert.Equal(CoffeeThemeMode.System, services.ThemePreferences.Load());
            await services.BeanService.CreateBeanAsync(new CreateBeanDto { Name = "Only two" });
            services.Preferences.SetLastDrinkType("Two");
            services.ThemePreferences.Save(CoffeeThemeMode.Light);
        }
        using (var reopened = new BaristaServices(first))
        {
            Assert.Equal("Only one", Assert.Single(reopened.Store.Beans).Name);
            Assert.Empty(reopened.Store.Shots);
            Assert.Equal("One", reopened.Preferences.GetLastDrinkType());
            Assert.Equal(TemperatureUnit.Celsius, reopened.Preferences.GetTemperatureUnit());
            Assert.Equal(CoffeeThemeMode.Dark, reopened.ThemePreferences.Load());
        }
        using (var reopened = new BaristaServices(second))
        {
            Assert.Equal("Only two", Assert.Single(reopened.Store.Beans).Name);
            Assert.Equal("Two", reopened.Preferences.GetLastDrinkType());
            Assert.Equal(CoffeeThemeMode.Light, reopened.ThemePreferences.Load());
        }
        Assert.Empty(theme.Keys);
        Assert.False(File.Exists(DefaultPath));
    }

    [Fact]
    public void ComparisonOpen_PreservesExistingRowsAndDefaultDatabaseBytes()
    {
        Directory.CreateDirectory(IOPath.GetDirectoryName(DefaultPath)!);
        using (var normal = new SqliteDataStore(DefaultPath))
            Assert.Single(normal.Shots);
        var defaultBytes = File.ReadAllBytes(DefaultPath);
        using (var existing = new SqliteDataStore(NamespacePath("existing")))
            Assert.Single(existing.Shots);

        var selected = Storage();
        selected.ConfigureComparisonDatabase(NamespacePath("existing"));
        using (var services = new BaristaServices(selected))
        {
            Assert.Single(services.Store.Shots);
            services.ThemePreferences.Save(CoffeeThemeMode.Dark);
        }
        using (var reopened = selected.OpenStore())
            Assert.Single(reopened.Shots);
        Assert.Equal(defaultBytes, File.ReadAllBytes(DefaultPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative.db")]
    [InlineData("../default/baristanotes.db")]
    public void InvalidSelection_ThrowsWithoutCreatingOrSelectingAStore(string? path)
    {
        var storage = Storage();
        Assert.ThrowsAny<ArgumentException>(() => storage.ConfigureComparisonDatabase(path!));
        Assert.False(File.Exists(DefaultPath));
        storage.ConfigureComparisonDatabase(NamespacePath("valid"));
        using var store = storage.OpenStore();
        Assert.Empty(store.Shots);
    }

    [Fact]
    public void DefaultPathAliasAndDirectory_RejectBeforeAnyDatabaseOpen()
    {
        var storage = Storage();
        Assert.Throws<ArgumentException>(() => storage.ConfigureComparisonDatabase(DefaultPath));
        Assert.Throws<ArgumentException>(() => storage.ConfigureComparisonDatabase(
            IOPath.Combine(_directory, "other", "..", "default", "baristanotes.db")));
        Assert.Throws<ArgumentException>(() => storage.ConfigureComparisonDatabase(_directory));
        Assert.False(File.Exists(DefaultPath));
    }

    [Fact]
    public void DuplicateSelection_ThrowsAndPreservesTheFirstNamespace()
    {
        var storage = Storage();
        storage.ConfigureComparisonDatabase(NamespacePath("first"));
        Assert.Throws<InvalidOperationException>(() => storage.ConfigureComparisonDatabase(NamespacePath("second")));
        using var opened = storage.OpenStore();
        Assert.Equal(NamespacePath("first"), opened.DatabasePath);
        Assert.False(File.Exists(NamespacePath("second")));
    }

    [Fact]
    public void LateSelection_AfterDefaultServices_ThrowsWithoutMovingData()
    {
        var storage = Storage(new ThemeStore());
        using var services = new BaristaServices(storage);
        Assert.Throws<InvalidOperationException>(() => storage.ConfigureComparisonDatabase(NamespacePath("late")));
        Assert.Single(services.Store.Shots);
        Assert.False(File.Exists(NamespacePath("late")));
    }

    [Fact]
    public void LateSelection_AfterThemeAccess_ThrowsBeforeDatabaseCreation()
    {
        var storage = Storage();
        storage.MarkResolutionStarted();
        Assert.Throws<InvalidOperationException>(() => storage.ConfigureComparisonDatabase(NamespacePath("late")));
        Assert.False(File.Exists(DefaultPath));
        Assert.False(File.Exists(NamespacePath("late")));

        _ = CoffeeTheme.Mode;
        Assert.Throws<InvalidOperationException>(() =>
            BaristaAppStorage.Current.ConfigureComparisonDatabase(NamespacePath("global-late")));
        Assert.False(File.Exists(NamespacePath("global-late")));
    }

    [Fact]
    public void ActiveComparison_CannotBeReboundByAnotherNamespaceOrDefaultCaller()
    {
        var first = Storage();
        var second = Storage();
        first.ConfigureComparisonDatabase(NamespacePath("first"));
        second.ConfigureComparisonDatabase(NamespacePath("second"));
        using (var services = new BaristaServices(first))
        {
            var boundBeans = BaristaServiceLocator.BeanService;
            Assert.Throws<InvalidOperationException>(() => new BaristaServices(second));
            using var other = new InMemoryDataStore();
            Assert.Throws<InvalidOperationException>(() =>
                BaristaServiceLocator.Initialize(other, new DataChangeNotifier()));
            Assert.Same(boundBeans, BaristaServiceLocator.BeanService);

            BaristaServiceLocator.Initialize(
                services.Store, services.DataChangeNotifier, services.Preferences);
            Assert.Throws<InvalidOperationException>(() =>
                BaristaServiceLocator.Initialize(other, new DataChangeNotifier()));
        }
        using var nowAllowed = new BaristaServices(second);
        Assert.Empty(nowAllowed.Store.Shots);
    }

    [Fact]
    public void ActiveDefault_CannotBeReplacedByComparison()
    {
        var selected = Storage();
        selected.ConfigureComparisonDatabase(NamespacePath("selected"));
        using (var normal = new BaristaServices(Storage(new ThemeStore())))
        {
            Assert.Throws<InvalidOperationException>(() => new BaristaServices(selected));
            Assert.Single(normal.Store.Shots);
        }
        using var allowed = new BaristaServices(selected);
        Assert.Empty(allowed.Store.Shots);
    }

    [Fact]
    public void WrongInjectedStore_IsRejectedWithoutReadingDefaultThemePreferences()
    {
        var selected = Storage();
        selected.ConfigureComparisonDatabase(NamespacePath("selected"));
        using var wrong = new SqliteDataStore(NamespacePath("wrong"), seedOnFirstRun: false);
        Assert.Throws<InvalidOperationException>(() => selected.CreateThemePreferences(wrong));
        Assert.False(File.Exists(DefaultPath));
    }

    [Fact]
    public void ComparisonBinding_RejectsADifferentCaseSensitiveNamespacePath()
    {
        var selected = Storage();
        selected.ConfigureComparisonDatabase(NamespacePath("selected"));
        using var wrong = new SqliteDataStore(
            IOPath.Combine(_directory, "selected", "BARISTANOTES.db"), seedOnFirstRun: false);
        Assert.Throws<InvalidOperationException>(() => selected.ValidateBinding(wrong));
        Assert.False(File.Exists(DefaultPath));
    }

    [Fact]
    public void OpenFailure_ThrowsInsteadOfFallingBackToDefaultStorage()
    {
        var parentFile = IOPath.Combine(_directory, "not-a-directory");
        File.WriteAllText(parentFile, "Preserve this file.");
        var selected = Storage();
        Assert.ThrowsAny<IOException>(() =>
        {
            selected.ConfigureComparisonDatabase(IOPath.Combine(parentFile, "baristanotes.db"));
            using var services = new BaristaServices(selected);
        });
        Assert.Equal("Preserve this file.", File.ReadAllText(parentFile));
        Assert.False(File.Exists(DefaultPath));
    }

    [Theory]
    [InlineData("Light", CoffeeThemeMode.Light)]
    [InlineData("Dark", CoffeeThemeMode.Dark)]
    [InlineData("System", CoffeeThemeMode.System)]
    [InlineData("invalid", CoffeeThemeMode.System)]
    public void DefaultThemeLoad_PreservesLegacyValueParsing(string value, CoffeeThemeMode expected)
    {
        var theme = new ThemeStore();
        theme.Set(BaristaThemePreferences.Key, value);
        using var services = new BaristaServices(Storage(theme));
        Assert.Equal(expected, services.ThemePreferences.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    sealed class ThemeStore : IPreferencesStore
    {
        readonly Dictionary<string, object> _values = new();
        public bool RejectAccess { get; init; }
        public List<string> Keys { get; } = new();

        T Read<T>(string key, T fallback)
        {
            Access(key);
            return _values.TryGetValue(key, out var value) ? (T)value : fallback;
        }

        void Write(string key, object value)
        {
            Access(key);
            _values[key] = value;
        }

        void Access(string key)
        {
            if (RejectAccess)
                throw new InvalidOperationException("Comparison code accessed default theme preferences.");
            Keys.Add(key);
        }

        public string? Get(string key, string? defaultValue) => Read(key, defaultValue);
        public void Set(string key, string value) => Write(key, value);
        public int Get(string key, int defaultValue) => Read(key, defaultValue);
        public void Set(string key, int value) => Write(key, value);
        public double Get(string key, double defaultValue) => Read(key, defaultValue);
        public void Set(string key, double value) => Write(key, value);
        public void Remove(string key) { Access(key); _values.Remove(key); }
    }
}
