using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Handlers.WPF;

public partial class ShellHandler
{
	Shell? _selectionShell;
	ShellItem? _selectedItem;
	ShellSection? _selectedSection;
	DispatcherOperation? _selectionUpdate;

	void ConnectShellSelection()
	{
		// ConnectHandler handles first attach; SetVirtualView also handles rebinds.
		if (!ReferenceEquals(_selectionShell, VirtualView))
		{
			DisconnectShellSelection();
			_selectionShell = VirtualView;
			_selectionShell.Navigated += OnShellNavigated;
			_selectionShell.Navigating += OnShellNavigating;
			_selectionShell.PropertyChanged += OnShellPropertyChanged;
			_selectionShell.PropertyChanged += OnSelectionShellPropertyChanged;
		}
		UpdateShellSelection();
	}

	void DisconnectShellSelection()
	{
		_selectionUpdate?.Abort();
		_selectionUpdate = null;
		if (_selectionShell != null)
		{
			_selectionShell.Navigated -= OnShellNavigated;
			_selectionShell.Navigating -= OnShellNavigating;
			_selectionShell.PropertyChanged -= OnShellPropertyChanged;
			_selectionShell.PropertyChanged -= OnSelectionShellPropertyChanged;
		}
		if (_selectedItem != null)
			_selectedItem.PropertyChanged -= OnSelectedItemPropertyChanged;
		if (_selectedSection != null)
			_selectedSection.PropertyChanged -= OnSelectedSectionPropertyChanged;
		_selectionShell = null;
		_selectedItem = null;
		_selectedSection = null;
	}

	void UpdateShellSelection()
	{
		var item = _selectionShell?.CurrentItem;
		if (!ReferenceEquals(_selectedItem, item))
		{
			if (_selectedItem != null)
				_selectedItem.PropertyChanged -= OnSelectedItemPropertyChanged;
			_selectedItem = item;
			if (_selectedItem != null)
				_selectedItem.PropertyChanged += OnSelectedItemPropertyChanged;
		}

		var section = item?.CurrentItem;
		if (!ReferenceEquals(_selectedSection, section))
		{
			if (_selectedSection != null)
				_selectedSection.PropertyChanged -= OnSelectedSectionPropertyChanged;
			_selectedSection = section;
			if (_selectedSection != null)
				_selectedSection.PropertyChanged += OnSelectedSectionPropertyChanged;
		}
	}

	void OnSelectionShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(Shell.CurrentItem))
			UpdateShellSelection();
	}

	void OnSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ShellItem.CurrentItem))
		{
			UpdateShellSelection();
			QueueSelectionUpdate();
		}
	}

	void OnSelectedSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ShellSection.CurrentItem))
			QueueSelectionUpdate();
	}

	void QueueSelectionUpdate()
	{
		if (_selectionShell == null || _selectionUpdate != null)
			return;

		// Materialize the selected template after Shell has finished updating its
		// selection, without waiting for Navigated (which needs that page to exist).
		_selectionUpdate = PlatformView.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
		{
			_selectionUpdate = null;
			if (_selectionShell != null)
				ShowCurrentPage();
		}));
	}
}
