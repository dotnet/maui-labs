using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

internal static class GtkNativeLibraries
{
	[ModuleInitializer]
	[SuppressMessage("Usage", "CA2255", Justification = "Native imports must resolve before startup guards and standalone custom handlers run.")]
	internal static void Initialize() =>
		NativeLibrary.SetDllImportResolver(typeof(GtkNativeLibraries).Assembly, Resolve);

	static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
	{
		var platformName = GetPlatformName(name, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
		return platformName == name ? IntPtr.Zero : NativeLibrary.Load(platformName, assembly, searchPath);
	}

	internal static string GetPlatformName(string name, bool windows, bool macOS) => name switch
	{
		"libgtk-4.so.1" when windows => "libgtk-4-1.dll",
		"libgtk-4.so.1" when macOS => "libgtk-4.1.dylib",
		"libcairo.so.2" when windows => "libcairo-2.dll",
		"libcairo.so.2" when macOS => "libcairo.2.dylib",
		"libfontconfig.so.1" when windows => "libfontconfig-1.dll",
		"libfontconfig.so.1" when macOS => "libfontconfig.1.dylib",
		"libpangocairo-1.0.so.0" when windows => "libpangocairo-1.0-0.dll",
		"libpangocairo-1.0.so.0" when macOS => "libpangocairo-1.0.0.dylib",
		"libpangoft2-1.0.so.0" when windows => "libpangoft2-1.0-0.dll",
		"libpangoft2-1.0.so.0" when macOS => "libpangoft2-1.0.0.dylib",
		_ => name
	};
}
