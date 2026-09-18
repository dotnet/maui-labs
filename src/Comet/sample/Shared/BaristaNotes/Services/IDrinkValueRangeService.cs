using System;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

public interface IDrinkValueRangeService
{
    event EventHandler? SettingsChanged;
    DrinkValueRangeSettingsSnapshot GetSettings();
    ValueRangeMode GetMode(DrinkValueMetric metric);
    EffectiveDrinkValueRange Resolve(DrinkValueMetric metric, BrewMethod method);
    void SetMode(DrinkValueMetric metric, ValueRangeMode mode);
    void SaveOverride(DrinkValueMetric metric, BrewMethod method, decimal minimum, decimal maximum);
    void RemoveOverride(DrinkValueMetric metric, BrewMethod method);
    void ResetOverrides(DrinkValueMetric metric);
}
