using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFAppInfo : IAppInfo
	{
		public string PackageName => AppDomain.CurrentDomain.FriendlyName;
		public string Name => Assembly.GetEntryAssembly()?.GetName().Name ?? "WPF App";
		public string VersionString => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
		public Version Version => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
		public string BuildString => VersionString;
		public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;
		public AppTheme RequestedTheme
		{
			get
			{
				try
				{
					using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
					if (key != null)
					{
						var value = key.GetValue("AppsUseLightTheme");
						if (value is int i)
							return i == 0 ? AppTheme.Dark : AppTheme.Light;
					}
				}
				catch { }
				return AppTheme.Light;
			}
		}
		public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;

		public void ShowSettingsUI() { }
	}
}
