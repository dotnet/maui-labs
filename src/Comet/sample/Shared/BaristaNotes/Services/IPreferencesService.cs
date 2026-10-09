using System.Collections.Generic;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

public interface IPreferencesService
{
    string? GetLastDrinkType();
    void SetLastDrinkType(string drinkType);
    int? GetLastBeanId();
    void SetLastBeanId(int? beanId);
    int? GetLastBagId();
    void SetLastBagId(int? bagId);
    int? GetLastMachineId();
    void SetLastMachineId(int? machineId);
    int? GetLastGrinderId();
    void SetLastGrinderId(int? grinderId);
    List<int> GetLastAccessoryIds();
    void SetLastAccessoryIds(List<int> accessoryIds);
    int? GetLastMadeById();
    void SetLastMadeById(int? madeById);
    int? GetLastMadeForId();
    void SetLastMadeForId(int? madeForId);
    decimal? GetLastDoseIn();
    void SetLastDoseIn(decimal? doseIn);
    int? GetLastGrindMicrons();
    void SetLastGrindMicrons(int? grindMicrons);
    decimal? GetLastExpectedTime();
    void SetLastExpectedTime(decimal? expectedTime);
    decimal? GetLastExpectedOutput();
    void SetLastExpectedOutput(decimal? expectedOutput);
    decimal? GetLastPreinfusionTime();
    void SetLastPreinfusionTime(decimal? preinfusionTime);
    TemperatureUnit GetTemperatureUnit();
    void SetTemperatureUnit(TemperatureUnit unit);
    string? GetDrinkValueRangeSettingsJson();
    void SetDrinkValueRangeSettingsJson(string json);
    void ClearAll();
}

public interface IPreferencesStore
{
    string? Get(string key, string? defaultValue);
    void Set(string key, string value);
    int Get(string key, int defaultValue);
    void Set(string key, int value);
    double Get(string key, double defaultValue);
    void Set(string key, double value);
    void Remove(string key);
}
