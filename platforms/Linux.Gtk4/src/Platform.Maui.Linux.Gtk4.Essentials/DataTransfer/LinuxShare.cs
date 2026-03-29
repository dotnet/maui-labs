using System.Diagnostics;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Platform.Maui.Linux.Gtk4.Essentials.DataTransfer;

public class LinuxShare : IShare
{
	public Task RequestAsync(ShareTextRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		// Best-effort: use xdg-open with a temporary file containing the text
		if (!string.IsNullOrEmpty(request.Uri))
		{
			Process.Start(new ProcessStartInfo("xdg-open", request.Uri)
				{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
		}
		else if (!string.IsNullOrEmpty(request.Text))
		{
			var tempFile = Path.Combine(Path.GetTempPath(), $"share_{Guid.NewGuid()}.txt");
			File.WriteAllText(tempFile, request.Text);
			Process.Start(new ProcessStartInfo("xdg-open", tempFile)
				{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
		}
		return Task.CompletedTask;
	}

	public Task RequestAsync(ShareFileRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.File is not null)
		{
			Process.Start(new ProcessStartInfo("xdg-open", request.File.FullPath)
				{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
		}
		return Task.CompletedTask;
	}

	public Task RequestAsync(ShareMultipleFilesRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		foreach (var file in request.Files ?? Enumerable.Empty<ShareFile>())
		{
			Process.Start(new ProcessStartInfo("xdg-open", file.FullPath)
				{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
		}
		return Task.CompletedTask;
	}
}
