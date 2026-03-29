using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.Gtk4.Essentials.Media;

public class LinuxMediaPicker : IMediaPicker
{
	public bool IsCaptureSupported => false;

	public Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options) =>
		PickFileAsync("Select Photo", "image/*");

	public Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options) =>
		PickFilesAsync("Select Photos", "image/*");

	public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options) =>
		throw new PlatformNotSupportedException("Photo capture is not supported on Linux desktop.");

	public Task<FileResult?> PickVideoAsync(MediaPickerOptions? options) =>
		PickFileAsync("Select Video", "video/*");

	public Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options) =>
		PickFilesAsync("Select Videos", "video/*");

	public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options) =>
		throw new PlatformNotSupportedException("Video capture is not supported on Linux desktop.");

	private static async Task<FileResult?> PickFileAsync(string title, string mimeType)
	{
		try
		{
			var dialog = Gtk.FileDialog.New();
			dialog.SetTitle(title);

			var filter = Gtk.FileFilter.New();
			filter.AddMimeType(mimeType);
			var filterList = Gio.ListStore.New(Gtk.FileFilter.GetGType());
			filterList.Append(filter);
			dialog.SetFilters(filterList);

			var window = (Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow();
			if (window is null) return null;

			var file = await dialog.OpenAsync(window);
			var path = file?.GetPath();
			return path is not null ? new FileResult(path) : null;
		}
		catch { return null; }
	}

	private static async Task<List<FileResult>> PickFilesAsync(string title, string mimeType)
	{
		try
		{
			var dialog = Gtk.FileDialog.New();
			dialog.SetTitle(title);

			var filter = Gtk.FileFilter.New();
			filter.AddMimeType(mimeType);
			var filterList = Gio.ListStore.New(Gtk.FileFilter.GetGType());
			filterList.Append(filter);
			dialog.SetFilters(filterList);

			var window = (Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow();
			if (window is null) return new List<FileResult>();

			var files = await dialog.OpenMultipleAsync(window);
			var results = new List<FileResult>();
			for (uint i = 0; i < files.GetNItems(); i++)
			{
				var gioFile = (Gio.File)files.GetObject(i)!;
				var path = gioFile.GetPath();
				if (path is not null)
					results.Add(new FileResult(path));
			}
			return results;
		}
		catch { return new List<FileResult>(); }
	}
}
