#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

/// <summary>
/// In-memory implementation of <see cref="IDrinkValueRangeService"/>
/// storing custom overrides in a dictionary — no persistence.
/// </summary>
public sealed class InMemoryDrinkValueRangeService : IDrinkValueRangeService
{
    readonly Dictionary<DrinkValueMetric, ValueRangeMode> _modes = new();
    readonly List<DrinkValueRangeOverride> _overrides = new();
    readonly IPreferencesService? _preferences;
    string? _loadWarning;

    public InMemoryDrinkValueRangeService(IPreferencesService? preferences = null)
    {
        _preferences = preferences;
        Load();
    }

    public event EventHandler? SettingsChanged;

    public DrinkValueRangeSettingsSnapshot GetSettings() => new(
        new Dictionary<DrinkValueMetric, ValueRangeMode>(_modes),
        _overrides.ToList(),
        _loadWarning);

    public ValueRangeMode GetMode(DrinkValueMetric metric) =>
        _modes.TryGetValue(metric, out var m) ? m : ValueRangeMode.Auto;

    public EffectiveDrinkValueRange Resolve(DrinkValueMetric metric, BrewMethod method)
    {
        var def = BrewMethodValueRangeCatalog.GetDefinition(method, metric);
        var mode = GetMode(metric);

        if (mode == ValueRangeMode.Custom)
        {
            var custom = _overrides.LastOrDefault(o => o.Metric == metric && o.Method == method);
            if (custom is not null)
            {
                var customRange = new DrinkValueRange(custom.Minimum, custom.Maximum);
                return new EffectiveDrinkValueRange(
                    customRange,
                    def.HardRange, customRange.Clamp(def.Default), def.Step, def.CanonicalUnit,
                    ValueRangeSource.Custom);
            }

            return new EffectiveDrinkValueRange(
                def.AutoRange, def.HardRange, def.AutoRange.Clamp(def.Default), def.Step, def.CanonicalUnit,
                ValueRangeSource.AutoFallback);
        }

        return new EffectiveDrinkValueRange(
            def.AutoRange, def.HardRange, def.AutoRange.Clamp(def.Default), def.Step, def.CanonicalUnit,
            ValueRangeSource.Auto);
    }

    public void SetMode(DrinkValueMetric metric, ValueRangeMode mode)
    {
        _modes[metric] = mode;
        Save();
    }

    public void SaveOverride(DrinkValueMetric metric, BrewMethod method, decimal minimum, decimal maximum)
    {
        ValidateOverride(metric, method, minimum, maximum);
        _overrides.RemoveAll(o => o.Metric == metric && o.Method == method);
        _overrides.Add(new DrinkValueRangeOverride(metric, method, minimum, maximum));
        if (GetMode(metric) != ValueRangeMode.Custom)
            _modes[metric] = ValueRangeMode.Custom;
        Save();
    }

    public void RemoveOverride(DrinkValueMetric metric, BrewMethod method)
    {
        _overrides.RemoveAll(o => o.Metric == metric && o.Method == method);
        Save();
    }

    public void ResetOverrides(DrinkValueMetric metric)
    {
        _overrides.RemoveAll(o => o.Metric == metric);
        Save();
    }

    private void Load()
    {
        var json = _preferences?.GetDrinkValueRangeSettingsJson();
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            var settings = JsonSerializer.Deserialize(json, BaristaJsonContext.Default.DrinkValueRangeSettings)
                ?? throw new JsonException("The range settings document was empty.");
            if (settings.SchemaVersion != DrinkValueRangeSettings.CurrentSchemaVersion)
            {
                _loadWarning = "Custom ranges use an unsupported format. Automatic ranges are active.";
                return;
            }

            if (settings.Modes is null
                || settings.Overrides is null
                || settings.Overrides.Any(item => item is null))
                throw new JsonException("The range settings document has invalid collections.");

            foreach (var item in settings.Overrides)
                ValidateOverride(item.Metric, item.Method, item.Minimum, item.Maximum);
            foreach (var mode in settings.Modes)
                _modes[mode.Key] = mode.Value;
            _overrides.AddRange(settings.Overrides);
        }
        catch (JsonException)
        {
            UseAutomaticRanges("Custom range settings could not be read. Automatic ranges are active.");
        }
        catch (ArgumentException)
        {
            UseAutomaticRanges("Custom range settings are invalid. Automatic ranges are active.");
        }
    }

    private void UseAutomaticRanges(string warning)
    {
        _modes.Clear();
        _overrides.Clear();
        _loadWarning = warning;
    }

    private void Save()
    {
        if (_preferences is not null)
        {
            var settings = new DrinkValueRangeSettings
            {
                Modes = new Dictionary<DrinkValueMetric, ValueRangeMode>(_modes),
                Overrides = _overrides.ToList()
            };
            _preferences.SetDrinkValueRangeSettingsJson(
                JsonSerializer.Serialize(settings, BaristaJsonContext.Default.DrinkValueRangeSettings));
        }
        _loadWarning = null;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ValidateOverride(
        DrinkValueMetric metric, BrewMethod method, decimal minimum, decimal maximum)
    {
        var definition = BrewMethodValueRangeCatalog.GetDefinition(method, metric);
        if (minimum >= maximum)
            throw new ArgumentException("Minimum must be less than maximum.");
        if (!definition.HardRange.Contains(minimum) || !definition.HardRange.Contains(maximum))
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                $"Range must stay between {definition.HardRange.Minimum} and {definition.HardRange.Maximum}.");
        var usesTenths = metric is DrinkValueMetric.DoseIn or DrinkValueMetric.Yield;
        if (usesTenths)
        {
            if (decimal.Round(minimum, 1) != minimum || decimal.Round(maximum, 1) != maximum)
                throw new ArgumentException("Dose and yield ranges support one decimal place.");
        }
        else if (decimal.Truncate(minimum) != minimum || decimal.Truncate(maximum) != maximum)
        {
            throw new ArgumentException("Grind and time ranges use whole numbers.");
        }
    }
}
