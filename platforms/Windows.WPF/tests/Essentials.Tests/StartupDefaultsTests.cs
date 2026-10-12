using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace Essentials.Tests;

public class StartupDefaultsTests
{
    [Fact]
    public void Startup_DeviceInfo_UsesCustomRegistrationOrDesktopFallback()
    {
        StaticFacadeTests.Run(() =>
        {
            var application = new TestApplication();
            try
            {
                var custom = new CustomDeviceInfo();
                using var services = new ServiceCollection().AddSingleton<IDeviceInfo>(custom).BuildServiceProvider();
                application.SetServices(services);
                ApplyStartupDefaults(application);
                Assert.Same(custom, DeviceInfo.Current);

                using var emptyServices = new ServiceCollection().BuildServiceProvider();
                application.SetServices(emptyServices);
                ApplyStartupDefaults(application);
                Assert.NotSame(custom, DeviceInfo.Current);
                Assert.Equal(DeviceIdiom.Desktop, DeviceInfo.Idiom);
            }
            finally
            {
                application.Shutdown();
            }
        });
    }

    static void ApplyStartupDefaults(TestApplication application)
    {
        var method = typeof(MauiWPFApplication).GetMethod("OverrideEssentialsDefaults", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(application, null);
    }

    sealed class TestApplication : MauiWPFApplication
    {
        public void SetServices(IServiceProvider services) => Services = services;
        protected override MauiApp CreateMauiApp() => throw new NotSupportedException();
    }

    sealed class CustomDeviceInfo : IDeviceInfo
    {
        public string Model => "Custom";
        public string Manufacturer => "Test";
        public string Name => "Custom device";
        public string VersionString => "1.0";
        public Version Version => new(1, 0);
        public DevicePlatform Platform => DevicePlatform.WinUI;
        public DeviceIdiom Idiom => DeviceIdiom.Desktop;
        public DeviceType DeviceType => DeviceType.Physical;
    }
}
