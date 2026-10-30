using System;
using System.Collections.Generic;
using System.Linq;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

/// <summary>
/// In-memory preferences store — no persistence across app restarts.
/// Sufficient for domain testing and the first app skeleton.
/// </summary>
public sealed class InMemoryPreferencesService : IPreferencesService
{
    private readonly Dictionary<string, string> _store = new();

    public string? GetLastDrinkType() => Get("LastDrinkType");
    public void SetLastDrinkType(string drinkType) => Set("LastDrinkType", drinkType);
    public int? GetLastBeanId() => GetInt("LastBeanId");
    public void SetLastBeanId(int? beanId) => SetNullableInt("LastBeanId", beanId);
    public int? GetLastBagId() => GetInt("LastBagId");
    public void SetLastBagId(int? bagId) => SetNullableInt("LastBagId", bagId);
    public int? GetLastMachineId() => GetInt("LastMachineId");
    public void SetLastMachineId(int? machineId) => SetNullableInt("LastMachineId", machineId);
    public int? GetLastGrinderId() => GetInt("LastGrinderId");
    public void SetLastGrinderId(int? grinderId) => SetNullableInt("LastGrinderId", grinderId);
    public List<int> GetLastAccessoryIds()
    {
        var val = Get("LastAccessoryIds");
        if (string.IsNullOrEmpty(val)) return new();
        return val.Split(',').Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
    }
    public void SetLastAccessoryIds(List<int> accessoryIds) => Set("LastAccessoryIds", string.Join(",", accessoryIds));
    public int? GetLastMadeById() => GetInt("LastMadeById");
    public void SetLastMadeById(int? madeById) => SetNullableInt("LastMadeById", madeById);
    public int? GetLastMadeForId() => GetInt("LastMadeForId");
    public void SetLastMadeForId(int? madeForId) => SetNullableInt("LastMadeForId", madeForId);
    public decimal? GetLastDoseIn() => GetDecimal("LastDoseIn");
    public void SetLastDoseIn(decimal? doseIn) => SetNullableDecimal("LastDoseIn", doseIn);
    public int? GetLastGrindMicrons() => GetInt("LastGrindMicrons");
    public void SetLastGrindMicrons(int? grindMicrons) => SetNullableInt("LastGrindMicrons", grindMicrons);
    public decimal? GetLastExpectedTime() => GetDecimal("LastExpectedTime");
    public void SetLastExpectedTime(decimal? expectedTime) => SetNullableDecimal("LastExpectedTime", expectedTime);
    public decimal? GetLastExpectedOutput() => GetDecimal("LastExpectedOutput");
    public void SetLastExpectedOutput(decimal? expectedOutput) => SetNullableDecimal("LastExpectedOutput", expectedOutput);
    public decimal? GetLastPreinfusionTime() => GetDecimal("LastPreinfusionTime");
    public void SetLastPreinfusionTime(decimal? preinfusionTime) => SetNullableDecimal("LastPreinfusionTime", preinfusionTime);
    public TemperatureUnit GetTemperatureUnit()
    {
        var val = Get("TemperatureUnit");
        return val != null && Enum.TryParse<TemperatureUnit>(val, out var u) ? u : TemperatureUnit.Fahrenheit;
    }
    public void SetTemperatureUnit(TemperatureUnit unit) => Set("TemperatureUnit", unit.ToString());
    public string? GetDrinkValueRangeSettingsJson() => Get("DrinkValueRangeSettings");
    public void SetDrinkValueRangeSettingsJson(string json) => Set("DrinkValueRangeSettings", json);

    public void ClearAll() => _store.Clear();

    private string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    private void Set(string key, string value) => _store[key] = value;
    private int? GetInt(string key)
    {
        var v = Get(key);
        return v != null && int.TryParse(v, out var i) ? i : null;
    }
    private void SetNullableInt(string key, int? val)
    {
        if (val.HasValue) Set(key, val.Value.ToString()); else _store.Remove(key);
    }
    private decimal? GetDecimal(string key)
    {
        var v = Get(key);
        return v != null && decimal.TryParse(v, out var d) ? d : null;
    }
    private void SetNullableDecimal(string key, decimal? val)
    {
        if (val.HasValue) Set(key, val.Value.ToString()); else _store.Remove(key);
    }
}
