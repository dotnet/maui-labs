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
    public IBeanService BeanService { get; }
    public IBagService BagService { get; }
    public IEquipmentService EquipmentService { get; }
    public IUserProfileService ProfileService { get; }
    public IDrinkValueRangeService RangeService { get; }
    public IImageProcessingService ImageProcessingService { get; }
    public Signal<TemperatureUnit> TemperatureUnit { get; }

    public BaristaServices()
        : this(new SqliteDataStore(SqliteDataStore.GetDefaultPath()))
    {
    }

    internal BaristaServices(IBaristaDataStore store)
    {
        Store = store;
        var notifier = new DataChangeNotifier();
        Preferences = store is SqliteDataStore sqlite
            ? new PreferencesService(sqlite.CreatePreferencesStore())
            : new InMemoryPreferencesService();
        TemperatureUnit = new Signal<TemperatureUnit>(Preferences.GetTemperatureUnit());
        DataChangeNotifier = notifier;
        ImageProcessingService = new LocalImageProcessingService();
        ShotService = new InMemoryShotService(Store, notifier, Preferences);
        BaristaServiceLocator.Initialize(Store, notifier, Preferences, ImageProcessingService);
        BeanService = BaristaServiceLocator.BeanService;
        BagService = BaristaServiceLocator.BagService;
        EquipmentService = BaristaServiceLocator.EquipmentService;
        ProfileService = BaristaServiceLocator.ProfileService;
        RangeService = BaristaServiceLocator.RangeService;
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
