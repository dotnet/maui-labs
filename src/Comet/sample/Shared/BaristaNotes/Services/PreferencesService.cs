using System.Collections.Generic;
using System.Text.Json;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

public sealed class PreferencesService(IPreferencesStore store) : IPreferencesService
{
    private const string LastDrinkType = "last_drink_type";
    private const string LastBeanId = "last_bean_id";
    private const string LastBagId = "last_bag_id";
    private const string LastMachineId = "last_machine_id";
    private const string LastGrinderId = "last_grinder_id";
    private const string LastAccessoryIds = "last_accessory_ids";
    private const string LastMadeById = "last_made_by_id";
    private const string LastMadeForId = "last_made_for_id";
    private const string LastDoseIn = "last_dose_in";
    private const string LastGrindMicrons = "last_grind_microns";
    private const string LastExpectedTime = "last_expected_time";
    private const string LastExpectedOutput = "last_expected_output";
    private const string LastPreinfusionTime = "last_preinfusion_time";
    private const string TemperatureUnitKey = "temperature_unit";
    private const string DrinkValueRangeSettings = "drink_value_range_settings";

    public string? GetLastDrinkType() => store.Get(LastDrinkType, null);
    public void SetLastDrinkType(string drinkType) => store.Set(LastDrinkType, drinkType);
    public int? GetLastBeanId() => NullableInt(LastBeanId);
    public void SetLastBeanId(int? value) => SetNullableInt(LastBeanId, value);
    public int? GetLastBagId() => NullableInt(LastBagId);
    public void SetLastBagId(int? value) => SetNullableInt(LastBagId, value);
    public int? GetLastMachineId() => NullableInt(LastMachineId);
    public void SetLastMachineId(int? value) => SetNullableInt(LastMachineId, value);
    public int? GetLastGrinderId() => NullableInt(LastGrinderId);
    public void SetLastGrinderId(int? value) => SetNullableInt(LastGrinderId, value);
    public List<int> GetLastAccessoryIds()
    {
        var json = store.Get(LastAccessoryIds, "[]");
        return json is null
            ? []
            : JsonSerializer.Deserialize(json, BaristaJsonContext.Default.ListInt32) ?? [];
    }
    public void SetLastAccessoryIds(List<int> value) =>
        store.Set(LastAccessoryIds, JsonSerializer.Serialize(value, BaristaJsonContext.Default.ListInt32));
    public int? GetLastMadeById() => NullableInt(LastMadeById);
    public void SetLastMadeById(int? value) => SetNullableInt(LastMadeById, value);
    public int? GetLastMadeForId() => NullableInt(LastMadeForId);
    public void SetLastMadeForId(int? value) => SetNullableInt(LastMadeForId, value);
    public decimal? GetLastDoseIn() => NullableDecimal(LastDoseIn);
    public void SetLastDoseIn(decimal? value) => SetNullableDouble(LastDoseIn, value);
    public int? GetLastGrindMicrons() => NullableInt(LastGrindMicrons);
    public void SetLastGrindMicrons(int? value)
    {
        if (value.HasValue) store.Set(LastGrindMicrons, value.Value); else store.Remove(LastGrindMicrons);
    }
    public decimal? GetLastExpectedTime() => NullableDecimal(LastExpectedTime);
    public void SetLastExpectedTime(decimal? value) => SetNullableDouble(LastExpectedTime, value);
    public decimal? GetLastExpectedOutput() => NullableDecimal(LastExpectedOutput);
    public void SetLastExpectedOutput(decimal? value) => SetNullableDouble(LastExpectedOutput, value);
    public decimal? GetLastPreinfusionTime() => NullableDecimal(LastPreinfusionTime);
    public void SetLastPreinfusionTime(decimal? value) => SetNullableDouble(LastPreinfusionTime, value);
    public TemperatureUnit GetTemperatureUnit() => (TemperatureUnit)store.Get(TemperatureUnitKey, (int)TemperatureUnit.Fahrenheit);
    public void SetTemperatureUnit(TemperatureUnit value) => store.Set(TemperatureUnitKey, (int)value);
    public string? GetDrinkValueRangeSettingsJson() => store.Get(DrinkValueRangeSettings, null);
    public void SetDrinkValueRangeSettingsJson(string json) => store.Set(DrinkValueRangeSettings, json);

    public void ClearAll()
    {
        foreach (var key in new[]
        {
            LastDrinkType, LastBeanId, LastBagId, LastMachineId, LastGrinderId,
            LastAccessoryIds, LastMadeById, LastMadeForId, LastDoseIn, LastGrindMicrons,
            LastExpectedTime, LastExpectedOutput, LastPreinfusionTime, TemperatureUnitKey,
            DrinkValueRangeSettings
        })
            store.Remove(key);
    }

    private int? NullableInt(string key)
    {
        var value = store.Get(key, -1);
        return value == -1 ? null : value;
    }

    private decimal? NullableDecimal(string key)
    {
        var value = store.Get(key, -1d);
        return value == -1d ? null : (decimal)value;
    }

    private void SetNullableInt(string key, int? value) => store.Set(key, value ?? -1);
    private void SetNullableDouble(string key, decimal? value) => store.Set(key, value.HasValue ? (double)value.Value : -1d);
}
