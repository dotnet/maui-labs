using System.Runtime.InteropServices;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFDeviceInfo : IDeviceInfo
	{
		public string Model => Environment.MachineName;
		public string Manufacturer => "PC";
		public string Name => Environment.MachineName;
		public string VersionString => Environment.OSVersion.VersionString;
		public Version Version => Environment.OSVersion.Version;
		public DevicePlatform Platform => DevicePlatform.WinUI;
		public DeviceIdiom Idiom => DeviceIdiom.Desktop;
		public DeviceType DeviceType => DeviceType.Physical;
	}
}
