#if NET11_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public sealed class ResourceFontRuntimeTests
{
	[GtkRuntimeFact]
	public void MauiFontOutput_RegistersWithGtkFontManager()
	{
		using var app = MauiApp.CreateBuilder()
			.UseMauiAppLinuxGtk4<Application>()
			.ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "AcceptanceOpenSans"))
			.Build();

		var manager = app.Services.GetRequiredService<IGtkFontManager>();
		var css = manager.BuildFontCss(Microsoft.Maui.Font.OfSize("AcceptanceOpenSans", 14));

		Assert.Contains("font-family:", css);
		Assert.Contains("Open Sans", css, StringComparison.OrdinalIgnoreCase);
	}
}
#endif
