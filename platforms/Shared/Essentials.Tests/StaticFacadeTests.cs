using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
#if WPF
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
using Microsoft.Maui.Controls.Hosting.WPF;
using BackendFallbackService = Microsoft.Maui.Devices.IDeviceInfo;
using EssentialsImplementation = Microsoft.Maui.Platforms.Windows.WPF.Essentials.WPFDeviceInfo;
#else
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using BackendFallbackService = Microsoft.Maui.ApplicationModel.IAppInfo;
using EssentialsImplementation = Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.AppModel.LinuxAppInfo;
#endif

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Essentials.Tests;

// The facades are assigned by MAUI 11's Essentials initializer (ConfigureEssentials) from the
// DI registrations the backend adds, and restored when the MauiApp is disposed.
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
                var implementation = app.Services.GetRequiredService(serviceType);
                Assert.Same(implementation, property.GetValue(null));
                Assert.StartsWith("Microsoft.Maui.Platforms.", implementation.GetType().Assembly.GetName().Name);
                output.WriteLine($"{facade.Name}.{property.Name} == DI {serviceType.Name} ({implementation.GetType().Name})");
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

    [Xunit.Fact]
    public void Dispose_RestoresTheFacadesItAssigned()
    {
        Run(() =>
        {
            var before = FileSystem.Current;
            var app = CreateBuilder().Build();
            var injected = app.Services.GetRequiredService<IFileSystem>();
            Assert.Same(injected, FileSystem.Current);
            app.Dispose();
            Assert.NotSame(injected, FileSystem.Current);
            Assert.Same(before, FileSystem.Current);
        });
    }

    [Xunit.Fact]
    public void Build_BackendOnly_UsesBackendFallback()
    {
        Run(() =>
        {
            using var app = AddBackend(MauiApp.CreateBuilder()).Build();
            var fallback = app.Services.GetRequiredService<BackendFallbackService>();
            Assert.Equal(GetBackendAssembly(), fallback.GetType().Assembly);
#if WPF
            Assert.Same(fallback, DeviceInfo.Current);
            Assert.Equal(DeviceIdiom.Desktop, DeviceInfo.Idiom);
#else
            Assert.Same(fallback, AppInfo.Current);
#endif
        });
    }

    [Xunit.Theory]
    [Xunit.InlineData(true)]
    [Xunit.InlineData(false)]
    public void Build_BackendAndEssentials_EssentialsReplaceBackendFallback(bool essentialsFirst)
    {
        Run(() =>
        {
            var builder = MauiApp.CreateBuilder();
            if (essentialsFirst)
                AddEssentials(builder);
            AddBackend(builder);
            if (!essentialsFirst)
                AddEssentials(builder);
            Assert.Single(builder.Services, d => d.ServiceType == typeof(BackendFallbackService));
            using var app = builder.Build();
            var implementation = Assert.IsType<EssentialsImplementation>(app.Services.GetRequiredService<BackendFallbackService>());
#if WPF
            Assert.Same(implementation, DeviceInfo.Current);
#else
            Assert.Same(implementation, AppInfo.Current);
#endif
        });
    }

    [Xunit.Fact]
    public void Build_BackendAndEssentials_KeepsAppRegistration()
    {
        Run(() =>
        {
            var builder = MauiApp.CreateBuilder();
            var custom = new TestFileSystem();
            builder.Services.AddSingleton<IFileSystem>(custom);
            AddEssentials(AddBackend(builder));
            using var app = builder.Build();
            Assert.Same(custom, FileSystem.Current);
        });
    }

    static MauiAppBuilder CreateBuilder()
    {
        var builder = MauiApp.CreateBuilder();
        AddEssentials(builder);
        return builder;
    }

    static MauiAppBuilder AddEssentials(MauiAppBuilder builder)
    {
#if WPF
        return builder.UseWPFEssentials();
#else
        return builder.AddLinuxGtk4Essentials();
#endif
    }

    static MauiAppBuilder AddBackend(MauiAppBuilder builder)
    {
#if WPF
        return builder.UseMauiAppWPF<TestApplication>();
#else
        return builder.UseMauiAppLinuxGtk4<TestApplication>();
#endif
    }

    static System.Reflection.Assembly GetBackendAssembly()
    {
#if WPF
        return typeof(Microsoft.Maui.Platforms.Windows.WPF.MauiWPFApplication).Assembly;
#else
        return typeof(Microsoft.Maui.Platforms.Linux.Gtk4.Platform.GtkThemeManager).Assembly;
#endif
    }

    // Runs each test on a fresh thread (STA for WPF). Facade state is restored by
    // disposing the MauiApp, which every test does.
    internal static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
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

    public sealed class TestApplication : Microsoft.Maui.Controls.Application;

    sealed class TestFileSystem : IFileSystem
    {
        public string AppDataDirectory => "custom-app-data";
        public string CacheDirectory => "custom-cache";
        public Task<Stream> OpenAppPackageFileAsync(string filename) => throw new NotSupportedException();
        public Task<bool> AppPackageFileExistsAsync(string filename) => Task.FromResult(false);
    }
}
