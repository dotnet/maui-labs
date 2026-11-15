using System;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Minimal IDeviceInfo that reports the Desktop idiom, registered in DI by UseMauiAppWPF so
	/// OnIdiom/OnPlatform resolve without the Essentials package. MAUI's Essentials initializer
	/// assigns the registration to the DeviceInfo facade; UseWPFEssentials() replaces it.
	/// </summary>
	sealed class WPFDeviceInfoFallback : IDeviceInfo
	{
		public string Model => "WPF";
		public string Manufacturer => "Microsoft";
		public string Name => Environment.MachineName;
		public string VersionString => Environment.OSVersion.VersionString;
		public Version Version => Environment.OSVersion.Version;
		public DevicePlatform Platform => DevicePlatform.WinUI;
		public DeviceIdiom Idiom => DeviceIdiom.Desktop;
		public DeviceType DeviceType => DeviceType.Physical;
	}
}
