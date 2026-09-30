using AppKit;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ShellHandler
{
	int _selectionGeneration;
	bool _selectionQueued;

	void ConnectShellEvents()
	{
		if (ReferenceEquals(_shell, VirtualView))
			return;
		DisconnectShellEvents();
		_shell = VirtualView;
		((INotifyCollectionChanged)_shell.Items).CollectionChanged += OnShellItemsChanged;
		_shell.Navigating += OnShellNavigating;
		_shell.Navigated += OnShellNavigated;
		_shell.PropertyChanged += OnShellPropertyChanged;
	}

	void DisconnectShellEvents()
	{
		_selectionGeneration++;
		_selectionQueued = false;
		if (_shell != null)
		{
			((INotifyCollectionChanged)_shell.Items).CollectionChanged -= OnShellItemsChanged;
			_shell.Navigating -= OnShellNavigating;
			_shell.Navigated -= OnShellNavigated;
			_shell.PropertyChanged -= OnShellPropertyChanged;
		}
		_shell = null;
	}

	internal void QueueSelectionUpdate(Element selection)
	{
		var shell = _shell;
		if (shell == null || _selectionQueued ||
			(!ReferenceEquals(shell.CurrentItem, selection) &&
			 !ReferenceEquals(shell.CurrentItem?.CurrentItem, selection)))
			return;

		_selectionQueued = true;
		var generation = _selectionGeneration;
		NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
		{
			// A disconnected/rebound handler must not execute an earlier selection.
			if (generation != _selectionGeneration || !ReferenceEquals(_shell, shell))
				return;
			_selectionQueued = false;
			ShowCurrentPage();
		});
	}
}
