using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.Gtk4.Essentials.Storage;

public class LinuxFilePicker : IFilePicker
{
	public async Task<FileResult?> PickAsync(PickOptions? options)
	{
		var results = await PickInternalAsync(false, options);
		return results?.FirstOrDefault();
	}

	public async Task<IEnumerable<FileResult>?> PickMultipleAsync(PickOptions? options)
	{
		return await PickInternalAsync(true, options);
	}

	private async Task<List<FileResult>?> PickInternalAsync(bool multiple, PickOptions? options)
	{
		var dialog = Gtk.FileDialog.New();
		dialog.SetTitle(options?.PickerTitle ?? "Select file");
		dialog.SetModal(true);

		if (options?.FileTypes is not null)
		{
			var filterList = Gio.ListStore.New(Gtk.FileFilter.GetGType());
			var filter = Gtk.FileFilter.New();
			foreach (var ft in options.FileTypes.Value)
			{
				var normalized = ft.Trim();
				if (string.IsNullOrWhiteSpace(normalized))
					continue;
				if (!normalized.StartsWith(".", StringComparison.Ordinal))
					normalized = $".{normalized}";
				filter.AddPattern($"*{normalized}");
				filter.AddSuffix(normalized.TrimStart('.'));
			}
			filterList.Append(filter);
			dialog.SetFilters(filterList);
		}

		var window = GetActiveWindow();
		if (window is null)
			return null;

		try
		{
			if (!multiple)
			{
				var file = await dialog.OpenAsync(window);
				var path = file?.GetPath();
				return path is not null ? new List<FileResult> { new FileResult(path) } : null;
			}

			var files = await dialog.OpenMultipleAsync(window);
			var results = new List<FileResult>();
			for (uint i = 0; i < files.GetNItems(); i++)
			{
				if (files.GetObject(i) is not Gio.File file)
					continue;

				var path = file.GetPath();
				if (path is not null)
					results.Add(new FileResult(path));
			}
			return results;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		catch (GLib.GException ex) when (ex.Message.Contains("dismissed", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
	}

	private static Gtk.Window? GetActiveWindow()
	{
		if (Gtk.Application.GetDefault() is Gtk.Application app && app.GetActiveWindow() is Gtk.Window activeWindow)
			return activeWindow;

		var toplevels = Gtk.Window.GetToplevels();
		for (uint i = 0; i < toplevels.GetNItems(); i++)
		{
			if (toplevels.GetObject(i) is Gtk.Window window)
				return window;
		}

		return null;
	}
}
