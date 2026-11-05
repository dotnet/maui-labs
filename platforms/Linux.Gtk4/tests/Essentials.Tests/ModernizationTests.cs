using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Devices;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;

namespace Essentials.Tests;

public class ModernizationTests
{
	[Fact]
	public void LinuxDeviceInfo_ReportsGtkRuntimeIdentity()
	{
		var deviceInfo = new LinuxDeviceInfo();

		Assert.Equal(DevicePlatform.Create("GTK"), deviceInfo.Platform);
	}

#if NET11_0_OR_GREATER
	[Fact]
	public void AddLinuxGtk4Essentials_UsesMauiDiBridge()
	{
		using var app = MauiApp.CreateBuilder()
			.AddLinuxGtk4Essentials()
			.Build();

		Assert.Same(app.Services.GetRequiredService<IDeviceInfo>(), DeviceInfo.Current);
		Assert.IsAssignableFrom<IViewScreenshot>(app.Services.GetRequiredService<IScreenshot>());
	}
#endif
}
