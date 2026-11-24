#if NET11_0_OR_GREATER
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

public sealed class GtkGesturePlatformManagerFactory : IGesturePlatformManagerFactory
{
	public IGesturePlatformManager CreateGesturePlatformManager(IViewHandler handler) =>
		new GtkGesturePlatformManager(handler);
}

internal sealed class GtkGesturePlatformManager : IGesturePlatformManager
{
	readonly View _view;
	readonly Gtk.Widget _widget;
	readonly INotifyCollectionChanged? _gestureRecognizers;
	bool _disposed;

	public GtkGesturePlatformManager(IViewHandler handler)
	{
		ArgumentNullException.ThrowIfNull(handler);

		_view = handler.VirtualView as View
			?? throw new ArgumentException("The GTK gesture manager requires a Microsoft.Maui.Controls.View.", nameof(handler));
		_widget = handler.ContainerView as Gtk.Widget
			?? handler.PlatformView as Gtk.Widget
			?? throw new ArgumentException("The GTK gesture manager requires a Gtk.Widget platform view.", nameof(handler));

		_gestureRecognizers = _view.GestureRecognizers as INotifyCollectionChanged;
		if (_gestureRecognizers is not null)
			_gestureRecognizers.CollectionChanged += OnGestureRecognizersChanged;
		GtkGestureExtensions.AttachGestures(_widget, _view);
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		if (_gestureRecognizers is not null)
			_gestureRecognizers.CollectionChanged -= OnGestureRecognizersChanged;
		GtkGestureExtensions.RemoveAttachedGestures(_widget);
	}

	void OnGestureRecognizersChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (!_disposed)
			GtkGestureExtensions.AttachGestures(_widget, _view);
	}
}
#endif
