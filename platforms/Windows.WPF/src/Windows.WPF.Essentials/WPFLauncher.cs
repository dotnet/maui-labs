using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFLauncher : ILauncher
	{
		public Task<bool> CanOpenAsync(Uri uri) => Task.FromResult(true);

		public Task<bool> OpenAsync(Uri uri)
		{
			Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
			return Task.FromResult(true);
		}

		public Task<bool> OpenAsync(OpenFileRequest request)
		{
			if (request?.File?.FullPath != null)
				Process.Start(new ProcessStartInfo(request.File.FullPath) { UseShellExecute = true });
			return Task.FromResult(true);
		}

		public Task<bool> TryOpenAsync(Uri uri)
		{
			try
			{
				Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
				return Task.FromResult(true);
			}
			catch
			{
				return Task.FromResult(false);
			}
		}
	}
}
