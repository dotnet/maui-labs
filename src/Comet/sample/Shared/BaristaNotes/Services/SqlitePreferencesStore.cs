using System;
using System.Globalization;
using CometBaristaNotes.Data;

namespace CometBaristaNotes.Services;

internal sealed class SqlitePreferencesStore(SqliteNativeDatabase database) : IPreferencesStore
{
    public string? Get(string key, string? defaultValue) => database.GetPreference(key) ?? defaultValue;
    public void Set(string key, string value) => database.SetPreference(key, value);
    public int Get(string key, int defaultValue) =>
        int.TryParse(database.GetPreference(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    public void Set(string key, int value) => database.SetPreference(key, value.ToString(CultureInfo.InvariantCulture));
    public double Get(string key, double defaultValue) =>
        double.TryParse(database.GetPreference(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    public void Set(string key, double value) => database.SetPreference(key, value.ToString("R", CultureInfo.InvariantCulture));
    public void Remove(string key) => database.RemovePreference(key);
}
