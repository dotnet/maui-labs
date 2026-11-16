#if NET11_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class ModernizationRegistrationTests
{
	[Fact]
	public void UseMauiAppLinuxGtk4_RegistersTypedGestureFactory()
	{
		using var app = MauiApp.CreateBuilder()
			.UseMauiAppLinuxGtk4<Application>()
			.Build();

		Assert.IsType<GtkGesturePlatformManagerFactory>(
			app.Services.GetRequiredService<IGesturePlatformManagerFactory>());
	}

	[Fact]
	public void UseMauiAppLinuxGtk4_RegistersModalNavigationFactory()
	{
		using var app = MauiApp.CreateBuilder()
			.UseMauiAppLinuxGtk4<Application>()
			.Build();

		Assert.IsType<GtkModalNavigationPlatformFactory>(
			app.Services.GetRequiredService<IModalNavigationPlatformFactory>());
	}

}
#endif
