using System.Globalization;
using Foundation;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class PreferencesImplementation : IPreferences
{
    static readonly object locker = new();

    public bool ContainsKey(string key, string? sharedName = null)
    {
        lock (locker)
            return GetUserDefaults(sharedName)[key] != null;
    }

    public void Remove(string key, string? sharedName = null)
    {
        lock (locker)
        {
            using var userDefaults = GetUserDefaults(sharedName);
            if (userDefaults[key] != null)
                userDefaults.RemoveObject(key);
        }
    }

    public void Clear(string? sharedName = null)
    {
        lock (locker)
        {
            using var userDefaults = GetUserDefaults(sharedName);
            var items = userDefaults.ToDictionary();
            foreach (var item in items.Keys)
            {
                if (item is NSString nsString)
                    userDefaults.RemoveObject(nsString);
            }
        }
    }

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        lock (locker)
        {
            using var userDefaults = GetUserDefaults(sharedName);
            if (value == null)
            {
                if (userDefaults[key] != null)
                    userDefaults.RemoveObject(key);
                return;
            }

            switch (value)
            {
                case string s: userDefaults.SetString(s, key); break;
                case int i: userDefaults.SetInt(i, key); break;
                case bool b: userDefaults.SetBool(b, key); break;
                case long l: userDefaults.SetString(Convert.ToString(l, CultureInfo.InvariantCulture), key); break;
                case double d: userDefaults.SetDouble(d, key); break;
                case float f: userDefaults.SetFloat(f, key); break;
                case DateTime dt: userDefaults.SetString(Convert.ToString(dt.ToBinary(), CultureInfo.InvariantCulture), key); break;
                case DateTimeOffset dto: userDefaults.SetString(dto.ToString("O"), key); break;
            }
        }
    }

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
    {
        object? value = null;

        lock (locker)
        {
            using var userDefaults = GetUserDefaults(sharedName);
            if (userDefaults[key] == null)
                return defaultValue;

            switch (defaultValue)
            {
                case int: value = (int)(nint)userDefaults.IntForKey(key); break;
                case bool: value = userDefaults.BoolForKey(key); break;
                case long: value = Convert.ToInt64(userDefaults.StringForKey(key), CultureInfo.InvariantCulture); break;
                case double: value = userDefaults.DoubleForKey(key); break;
                case float: value = userDefaults.FloatForKey(key); break;
                case DateTime:
                    var savedDt = userDefaults.StringForKey(key);
                    value = DateTime.FromBinary(Convert.ToInt64(savedDt, CultureInfo.InvariantCulture));
                    break;
                case DateTimeOffset:
                    var savedDto = userDefaults.StringForKey(key);
                    if (DateTimeOffset.TryParse(savedDto, out var dto))
                        value = dto;
                    break;
                case string: value = userDefaults.StringForKey(key); break;
                default:
                    if (typeof(T) == typeof(string))
                        value = userDefaults.StringForKey(key);
                    break;
            }
        }

        return value is T t ? t : defaultValue;
    }

    static NSUserDefaults GetUserDefaults(string? sharedName)
    {
        if (!string.IsNullOrWhiteSpace(sharedName))
            return new NSUserDefaults(sharedName!, NSUserDefaultsType.SuiteName);
        return NSUserDefaults.StandardUserDefaults;
    }
}
