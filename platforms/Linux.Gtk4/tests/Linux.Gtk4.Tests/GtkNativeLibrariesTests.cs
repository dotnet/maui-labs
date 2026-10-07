using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class GtkNativeLibrariesTests
{
	[Theory]
	[InlineData("libgtk-4.so.1", "libgtk-4-1.dll", "libgtk-4.1.dylib")]
	[InlineData("libcairo.so.2", "libcairo-2.dll", "libcairo.2.dylib")]
	[InlineData("libfontconfig.so.1", "libfontconfig-1.dll", "libfontconfig.1.dylib")]
	[InlineData("libpangocairo-1.0.so.0", "libpangocairo-1.0-0.dll", "libpangocairo-1.0.0.dylib")]
	[InlineData("libpangoft2-1.0.so.0", "libpangoft2-1.0-0.dll", "libpangoft2-1.0.0.dylib")]
	public void NativeImports_ResolvePlatformLibraryNames(string linux, string windows, string macOS)
	{
		Assert.Equal(linux, GtkNativeLibraries.GetPlatformName(linux, false, false));
		Assert.Equal(windows, GtkNativeLibraries.GetPlatformName(linux, true, false));
		Assert.Equal(macOS, GtkNativeLibraries.GetPlatformName(linux, false, true));
	}

	[Fact]
	public void UnrelatedImports_KeepDefaultResolution() =>
		Assert.Equal("other", GtkNativeLibraries.GetPlatformName("other", true, false));
}
