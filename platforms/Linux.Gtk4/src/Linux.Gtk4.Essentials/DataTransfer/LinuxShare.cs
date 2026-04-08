using System.Diagnostics;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.DataTransfer;

public class LinuxShare : IShare
{
	public Task RequestAsync(ShareTextRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		// Best-effort: use xdg-open with a temporary file containing the text
		if (!string.IsNullOrEmpty(request.Uri))
		{
			var psi = new ProcessStartInfo("xdg-open")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			psi.ArgumentList.Add(request.Uri);
			using var process = Process.Start(psi);
		}
		else if (!string.IsNullOrEmpty(request.Text))
		{
			var tempFile = Path.Combine(Path.GetTempPath(), $"share_{Guid.NewGuid()}.txt");
			File.WriteAllText(tempFile, request.Text);
			try
			{
				var textPsi = new ProcessStartInfo("xdg-open")
				{
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				textPsi.ArgumentList.Add(tempFile);
				using var textProcess = Process.Start(textPsi);
			}
			finally
			{
				try { File.Delete(tempFile); } catch { }
			}
		}
		return Task.CompletedTask;
	}

	public Task RequestAsync(ShareFileRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.File is not null)
		{
			var psi = new ProcessStartInfo("xdg-open")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			psi.ArgumentList.Add(request.File.FullPath);
			using var process = Process.Start(psi);
		}
		return Task.CompletedTask;
	}

	public Task RequestAsync(ShareMultipleFilesRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		foreach (var file in request.Files ?? Enumerable.Empty<ShareFile>())
		{
			var psi = new ProcessStartInfo("xdg-open")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			psi.ArgumentList.Add(file.FullPath);
			using var process = Process.Start(psi);
		}
		return Task.CompletedTask;
	}
}
