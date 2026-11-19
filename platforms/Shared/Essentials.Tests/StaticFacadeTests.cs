using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
#if WPF
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
#else
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
#endif

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Essentials.Tests;

public class StaticFacadeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Xunit.Fact]
    public void Build_StaticCalls_UsePlatformImplementations()
    {
        Run(() =>
        {
            using var app = CreateBuilder().Build();
            Assert.False(string.IsNullOrWhiteSpace(FileSystem.AppDataDirectory));
            Assert.Equal(app.Services.GetRequiredService<IFileSystem>().AppDataDirectory, FileSystem.AppDataDirectory);
            Assert.Equal(DeviceIdiom.Desktop, DeviceInfo.Idiom);
            output.WriteLine($"FileSystem.AppDataDirectory={FileSystem.AppDataDirectory}; DeviceInfo.Idiom={DeviceInfo.Idiom}");
#if WPF
            var display = DeviceDisplay.MainDisplayInfo;
            Assert.True(display.Width > 0);
            Assert.True(display.Height > 0);
            Assert.Equal(app.Services.GetRequiredService<IDeviceDisplay>().MainDisplayInfo, display);
            output.WriteLine($"DeviceDisplay.MainDisplayInfo={display}");
#endif

            var sharedName = $"facade-test-{Guid.NewGuid():N}";
            try
            {
                Preferences.Set("key", "static", sharedName);
                Assert.Equal("static", app.Services.GetRequiredService<IPreferences>().Get("key", "", sharedName));
                app.Services.GetRequiredService<IPreferences>().Set("key", "injected", sharedName);
                Assert.Equal("injected", Preferences.Get("key", "", sharedName));
                output.WriteLine("Preferences round trip: static -> DI -> static");
            }
            finally
            {
                app.Services.GetRequiredService<IPreferences>().Clear(sharedName);
            }
        });
    }

    [Xunit.Fact]
    public void Build_EveryRegisteredEssentialsInterface_SharesItsStaticInstance()
    {
        Run(() =>
        {
            var builder = CreateBuilder();
            var interfaces = builder.Services.Select(s => s.ServiceType)
                .Where(t => t.IsInterface && t.Assembly == typeof(IFileSystem).Assembly)
                .Distinct().ToArray();
            Assert.True(interfaces.Length >= 35);
            using var app = builder.Build();
            foreach (var serviceType in interfaces)
            {
                var facade = serviceType.Assembly.GetType($"{serviceType.Namespace}.{serviceType.Name[1..]}", throwOnError: true)!;
                var property = facade.GetProperty("Current") ?? facade.GetProperty("Default");
                Assert.NotNull(property);
                Assert.Same(app.Services.GetRequiredService(serviceType), property.GetValue(null));
                output.WriteLine($"{facade.Name}.{property.Name} == DI {serviceType.Name}");
            }
        });
    }

    [Xunit.Theory]
    [Xunit.InlineData(true)]
    [Xunit.InlineData(false)]
    public void Build_CustomRegistrationBeforeOrAfterExtension_IsUsedByStatics(bool before)
    {
        Run(() =>
        {
            var builder = MauiApp.CreateBuilder();
            var custom = new TestFileSystem();
            if (before)
                builder.Services.AddSingleton<IFileSystem>(custom);
            AddEssentials(builder);
            AddEssentials(builder);
            if (!before)
                builder.Services.AddSingleton<IFileSystem>(custom);
            using var app = builder.Build();
            Assert.Same(custom, FileSystem.Current);
            Assert.Same(custom, app.Services.GetRequiredService<IFileSystem>());
            Assert.Equal("custom-app-data", FileSystem.AppDataDirectory);
        });
    }

    static MauiAppBuilder CreateBuilder()
    {
        var builder = MauiApp.CreateBuilder();
        AddEssentials(builder);
        return builder;
    }

    static void AddEssentials(MauiAppBuilder builder)
    {
#if WPF
        builder.UseWPFEssentials();
#else
        builder.AddLinuxGtk4Essentials();
#endif
    }

    internal static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var fields = typeof(FileSystem).Assembly.GetTypes()
                .Where(t => t.IsAbstract && t.IsSealed)
                .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                .Where(f => f.Name is "currentImplementation" or "defaultImplementation")
                .Select(f => (Field: f, Value: f.GetValue(null))).ToArray();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                foreach (var (field, value) in fields)
                    field.SetValue(null, value);
            }
        });
#if WPF
        thread.SetApartmentState(ApartmentState.STA);
#endif
        thread.Start();
        thread.Join();
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    sealed class TestFileSystem : IFileSystem
    {
        public string AppDataDirectory => "custom-app-data";
        public string CacheDirectory => "custom-cache";
        public Task<Stream> OpenAppPackageFileAsync(string filename) => throw new NotSupportedException();
        public Task<bool> AppPackageFileExistsAsync(string filename) => Task.FromResult(false);
    }
}
