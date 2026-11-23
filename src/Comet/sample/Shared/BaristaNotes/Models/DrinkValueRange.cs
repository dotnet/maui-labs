using System;
using System.Collections.Generic;

using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Models;

public sealed record DrinkValueRange(decimal Minimum, decimal Maximum)
{
    public bool Contains(decimal value) => value >= Minimum && value <= Maximum;
    public decimal Clamp(decimal value) => Math.Clamp(value, Minimum, Maximum);
}

public sealed record DrinkValueRangeDefinition(
    DrinkValueRange AutoRange,
    DrinkValueRange HardRange,
    decimal Default,
    decimal Step,
    string CanonicalUnit);

public sealed record EffectiveDrinkValueRange(
    DrinkValueRange Range,
    DrinkValueRange HardRange,
    decimal Default,
    decimal Step,
    string CanonicalUnit,
    ValueRangeSource Source);

public sealed record DrinkValueRangeOverride(
    DrinkValueMetric Metric,
    BrewMethod Method,
    decimal Minimum,
    decimal Maximum);

public sealed class DrinkValueRangeSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Dictionary<DrinkValueMetric, ValueRangeMode> Modes { get; init; } = [];
    public List<DrinkValueRangeOverride> Overrides { get; init; } = [];
}

public sealed record DrinkValueRangeSettingsSnapshot(
    IReadOnlyDictionary<DrinkValueMetric, ValueRangeMode> Modes,
    IReadOnlyList<DrinkValueRangeOverride> Overrides,
    string? LoadWarning);
