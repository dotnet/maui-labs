using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace Essentials.Tests;

public class StartupDefaultsTests
{
    [Fact]
    public void Build_DeviceInfo_UsesRegisteredWpfIdentity()
    {
        StaticFacadeTests.Run(() =>
        {
            using var app = MauiApp.CreateBuilder().UseWPFEssentials().Build();
            var deviceInfo = app.Services.GetRequiredService<IDeviceInfo>();

            Assert.Same(deviceInfo, DeviceInfo.Current);
            Assert.Equal(DeviceIdiom.Desktop, DeviceInfo.Idiom);
            Assert.Equal(DevicePlatform.Create("WPF"), DeviceInfo.Platform);
        });
    }
}
