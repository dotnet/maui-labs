#nullable enable
using System;
using CometSamples.BaristaNotes.Styles;

namespace CometBaristaNotes.Services;

public sealed class BaristaThemePreferences
{
    public const string Key = "baristanotes_theme";
    readonly IPreferencesStore _store;

    internal BaristaThemePreferences(IPreferencesStore store) => _store = store;

    public CoffeeThemeMode Load()
    {
        var saved = _store.Get(Key, "System");
        return Enum.TryParse<CoffeeThemeMode>(saved, out var mode)
            ? mode
            : CoffeeThemeMode.System;
    }

    public void Save(CoffeeThemeMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        _store.Set(Key, mode.ToString());
    }
}

internal sealed class MauiPreferencesStore : IPreferencesStore
{
    public string? Get(string key, string? defaultValue) => Microsoft.Maui.Storage.Preferences.Get(key, defaultValue);
    public void Set(string key, string value) => Microsoft.Maui.Storage.Preferences.Set(key, value);
    public int Get(string key, int defaultValue) => Microsoft.Maui.Storage.Preferences.Get(key, defaultValue);
    public void Set(string key, int value) => Microsoft.Maui.Storage.Preferences.Set(key, value);
    public double Get(string key, double defaultValue) => Microsoft.Maui.Storage.Preferences.Get(key, defaultValue);
    public void Set(string key, double value) => Microsoft.Maui.Storage.Preferences.Set(key, value);
    public void Remove(string key) => Microsoft.Maui.Storage.Preferences.Remove(key);
}
