using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
using Microsoft.Maui.Storage;

namespace Essentials.Tests;

public class VersionTrackingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_DoesNotRecordVersionUntilTrackingIsUsed(bool queryFirst)
    {
        StaticFacadeTests.Run(() =>
        {
            var preferences = new RecordingPreferences();
            var builder = MauiApp.CreateBuilder().UseWPFEssentials();
            builder.Services.AddSingleton<IPreferences>(preferences);
            using var app = builder.Build();

            Assert.Same(app.Services.GetRequiredService<IVersionTracking>(), VersionTracking.Default);
            Assert.Equal(0, preferences.Writes);
            if (queryFirst)
                Assert.True(VersionTracking.IsFirstLaunchEver);
            else
                VersionTracking.Track();
            Assert.True(preferences.Writes > 0);
            Assert.True(VersionTracking.IsFirstLaunchEver);

            var writes = preferences.Writes;
            VersionTracking.Track();
            Assert.NotEmpty(VersionTracking.VersionHistory);
            Assert.Equal(writes, preferences.Writes);
        });
    }

    sealed class RecordingPreferences : IPreferences
    {
        readonly Dictionary<(string? SharedName, string Key), object?> _values = new();
        public int Writes { get; private set; }
        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey((sharedName, key));
        public void Remove(string key, string? sharedName = null) => _values.Remove((sharedName, key));
        public void Clear(string? sharedName = null)
        {
            foreach (var key in _values.Keys.Where(k => k.SharedName == sharedName).ToArray())
                _values.Remove(key);
        }
        public void Set<T>(string key, T value, string? sharedName = null)
        {
            _values[(sharedName, key)] = value;
            Writes++;
        }
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
            => _values.TryGetValue((sharedName, key), out var value) ? (T)value! : defaultValue;
    }
}
