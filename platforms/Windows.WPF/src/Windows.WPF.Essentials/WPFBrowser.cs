using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFBrowser : IBrowser
	{
		public Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
		{
			Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
			return Task.FromResult(true);
		}
	}
}
