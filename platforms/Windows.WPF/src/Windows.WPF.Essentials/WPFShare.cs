using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFShare : IShare
	{
		public Task RequestAsync(ShareTextRequest request)
		{
			if (!string.IsNullOrEmpty(request.Uri))
				System.Windows.Clipboard.SetText(request.Uri);
			else if (!string.IsNullOrEmpty(request.Text))
				System.Windows.Clipboard.SetText(request.Text);
			return Task.CompletedTask;
		}

		public Task RequestAsync(ShareFileRequest request) => Task.CompletedTask;

		public Task RequestAsync(ShareMultipleFilesRequest request) => Task.CompletedTask;
	}
}
