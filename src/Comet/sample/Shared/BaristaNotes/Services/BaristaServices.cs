#nullable enable
using System;
using Comet;
using Comet.Reactive;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;

namespace CometSamples.BaristaNotes;

public sealed class BaristaServices : IDisposable
{
    bool _disposed;

    public IShotService ShotService { get; }
    public IDataChangeNotifier DataChangeNotifier { get; }
    public IBaristaDataStore Store { get; }
    public IPreferencesService Preferences { get; }
    public BaristaThemePreferences ThemePreferences { get; }
    public IBeanService BeanService { get; }
    public IBagService BagService { get; }
    public IEquipmentService EquipmentService { get; }
    public IUserProfileService ProfileService { get; }
    public IDrinkValueRangeService RangeService { get; }
    public IImageProcessingService ImageProcessingService { get; }
    public Signal<TemperatureUnit> TemperatureUnit { get; }

    public BaristaServices()
        : this(BaristaAppStorage.Current)
    {
    }

    internal BaristaServices(IBaristaDataStore store)
        : this(store, BaristaAppStorage.Current)
    {
    }

    internal BaristaServices(BaristaAppStorage storage)
        : this(storage.OpenStore(), storage)
    {
    }

    BaristaServices(IBaristaDataStore store, BaristaAppStorage storage)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        try
        {
            var exclusiveStore = storage.ValidateBinding(Store);
            var notifier = new DataChangeNotifier();
            Preferences = store is SqliteDataStore sqlite
                ? new PreferencesService(sqlite.CreatePreferencesStore())
                : new InMemoryPreferencesService();
            ThemePreferences = storage.CreateThemePreferences(Store);
            TemperatureUnit = new Signal<TemperatureUnit>(Preferences.GetTemperatureUnit());
            DataChangeNotifier = notifier;
            ImageProcessingService = new LocalImageProcessingService();
            ShotService = new InMemoryShotService(Store, notifier, Preferences);
            BaristaServiceLocator.Initialize(
                Store, notifier, Preferences, ImageProcessingService,
                requireExclusiveStore: exclusiveStore);
            BeanService = BaristaServiceLocator.BeanService;
            BagService = BaristaServiceLocator.BagService;
            EquipmentService = BaristaServiceLocator.EquipmentService;
            ProfileService = BaristaServiceLocator.ProfileService;
            RangeService = BaristaServiceLocator.RangeService;
        }
        catch
        {
            BaristaServiceLocator.Reset(Store);
            Store.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        BaristaServiceLocator.Reset(Store);
        Store.Dispose();
    }
}
