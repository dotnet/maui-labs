#if NET11_0_OR_GREATER
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

public sealed class GtkModalNavigationPlatformFactory : IModalNavigationPlatformFactory
{
	public IModalNavigationPlatform CreateModalNavigationPlatform(IModalNavigationHost host) =>
		new GtkModalNavigationPlatform(host);
}

internal sealed class GtkModalNavigationPlatform : IModalNavigationPlatform
{
	readonly IModalNavigationHost _host;
	readonly Gtk.Window _window;
	readonly WindowRootViewContainer? _container;
	readonly Dictionary<Page, Gtk.Window> _dialogs = new();
	bool _disposed;

	public GtkModalNavigationPlatform(IModalNavigationHost host)
	{
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_window = host.Window.Handler?.PlatformView as Gtk.Window
			?? throw new InvalidOperationException("GTK modal navigation requires a connected Gtk.Window.");
		_container = _window.GetChild() as WindowRootViewContainer;
	}

	public bool IsReady => !_disposed && _host.IsWindowReady;

	public Task PushModalAsync(Page modal, bool animated)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		var platformContent = (Gtk.Widget)modal.ToPlatform(_host.MauiContext);
		if (GtkPage.GetModalPresentationStyle(modal) == GtkModalPresentationStyle.Inline)
		{
			(_container ?? throw new InvalidOperationException("GTK inline modal navigation requires a window root container."))
				.PushModal(platformContent);
			return Task.CompletedTask;
		}

		var dialog = new Gtk.Window();
		dialog.SetModal(true);
		dialog.SetTransientFor(_window);
		dialog.SetTitle(modal.Title ?? string.Empty);

		var (width, height) = ComputeDialogSize(modal);
		platformContent.SetSizeRequest((int)width, (int)height);

		var minimumWidth = GtkPage.GetModalMinWidth(modal);
		var minimumHeight = GtkPage.GetModalMinHeight(modal);
		if (minimumWidth > 0 || minimumHeight > 0)
		{
			dialog.SetSizeRequest(
				minimumWidth > 0 ? (int)minimumWidth : -1,
				minimumHeight > 0 ? (int)minimumHeight : -1);
		}

		if (_window.GetApplication() is { } application)
			dialog.SetApplication(application);

		platformContent.SetVexpand(true);
		platformContent.SetHexpand(true);
		dialog.SetChild(platformContent);
		_dialogs[modal] = dialog;

		dialog.OnCloseRequest += (_, _) =>
		{
			if (!_dialogs.Remove(modal))
				return false;

			_ = _host.Window.Navigation.PopModalAsync(animated: false);
			return false;
		};

		dialog.Present();
		return Task.CompletedTask;
	}

	public Task PopModalAsync(Page modal, bool animated)
	{
		if (_disposed)
			return Task.CompletedTask;

		if (_dialogs.Remove(modal, out var dialog))
		{
			dialog.SetChild(null);
			dialog.Close();
		}
		else if (GtkPage.GetModalPresentationStyle(modal) == GtkModalPresentationStyle.Inline)
		{
			_container?.PopModal();
		}

		return Task.CompletedTask;
	}

	public void PageAttached()
	{
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		foreach (var dialog in _dialogs.Values)
		{
			dialog.SetChild(null);
			dialog.Close();
		}
		_dialogs.Clear();

		while (_container?.PopModal() is not null)
		{
		}
	}

	(double Width, double Height) ComputeDialogSize(Page page)
	{
		_window.GetDefaultSize(out var nativeWidth, out var nativeHeight);
		double parentWidth = nativeWidth > 0 ? nativeWidth : 800;
		double parentHeight = nativeHeight > 0 ? nativeHeight : 600;
		var requestedWidth = GtkPage.GetModalWidth(page);
		var requestedHeight = GtkPage.GetModalHeight(page);
		double width = requestedWidth > 0 ? requestedWidth : parentWidth;
		double height = requestedHeight > 0 ? requestedHeight : parentHeight;

		if (GtkPage.GetModalSizesToContent(page) && (requestedWidth <= 0 || requestedHeight <= 0) &&
			page is ContentPage { Content: IView content })
		{
			var measured = content.Measure(double.PositiveInfinity, double.PositiveInfinity);
			var padding = page.Padding;
			if (requestedWidth <= 0)
				width = measured.Width + padding.Left + padding.Right;
			if (requestedHeight <= 0)
				height = measured.Height + padding.Top + padding.Bottom;
		}

		var minimumWidth = GtkPage.GetModalMinWidth(page);
		var minimumHeight = GtkPage.GetModalMinHeight(page);
		if (minimumWidth > 0)
			width = Math.Max(width, minimumWidth);
		if (minimumHeight > 0)
			height = Math.Max(height, minimumHeight);

		return (Math.Min(width, parentWidth), Math.Min(height, parentHeight));
	}
}
#endif
