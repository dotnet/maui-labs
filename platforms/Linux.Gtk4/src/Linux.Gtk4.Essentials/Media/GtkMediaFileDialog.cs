namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

internal static class GtkMediaFileDialog
{
	delegate void Open(nint dialog, nint parent, nint cancellable, Gio.Internal.AsyncReadyCallback callback, nint data);
	delegate nint Finish(nint dialog, nint result, out GLib.Internal.ErrorOwnedHandle error);

	internal static Task<Gio.File?> OpenAsync(Gtk.FileDialog dialog, Gtk.Window parent) =>
		SelectAsync(dialog, parent, Gtk.Internal.FileDialog.Open, Gtk.Internal.FileDialog.OpenFinish,
			handle => (Gio.File)GObject.Internal.InstanceWrapper.WrapHandle<Gio.FileHelper>(handle, true));

	internal static Task<Gio.ListModel?> OpenMultipleAsync(Gtk.FileDialog dialog, Gtk.Window parent) =>
		SelectAsync(dialog, parent, Gtk.Internal.FileDialog.OpenMultiple, Gtk.Internal.FileDialog.OpenMultipleFinish,
			handle => (Gio.ListModel)GObject.Internal.InstanceWrapper.WrapHandle<Gio.ListModelHelper>(handle, true));

	static Task<T?> SelectAsync<T>(
		Gtk.FileDialog dialog, Gtk.Window parent, Open open, Finish finish, Func<nint, T> wrap) where T : class
	{
		var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var callback = new Gio.Internal.AsyncReadyCallbackAsyncHandler((source, result, _) =>
		{
			try
			{
				if (source is null)
					throw new InvalidOperationException("The GTK media dialog returned no source object.");
				var handle = finish(source.Handle.DangerousGetHandle(), result.Handle.DangerousGetHandle(), out var error);
				completion.SetResult(GetResult(handle, error, wrap));
			}
			catch (Exception ex)
			{
				completion.SetException(ex);
			}
		});
		open(dialog.Handle.DangerousGetHandle(), parent.Handle.DangerousGetHandle(), 0, callback.NativeCallback, 0);
		return completion.Task;
	}

	internal static T? GetResult<T>(nint handle, GLib.Internal.ErrorOwnedHandle error, Func<nint, T> wrap) where T : class
	{
		// GirCore's Task wrappers discard the GError domain/code when creating GException.
		// Inspect them at the native finish boundary, independent of translated messages.
		if (!error.IsInvalid)
		{
			var domain = GLib.Functions.QuarkFromString("gtk-dialog-error-quark");
			if (GLib.Internal.Error.Matches(error, domain, (int)Gtk.DialogError.Dismissed) ||
				GLib.Internal.Error.Matches(error, domain, (int)Gtk.DialogError.Cancelled))
			{
				error.Dispose();
				return null;
			}
			throw new GLib.GException(error);
		}

		error.Dispose();
		return handle == 0 ? null : wrap(handle);
	}
}
