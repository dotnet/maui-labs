using System.Collections.Specialized;
using System.Windows.Threading;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Handlers.WPF;

public partial class ShellHandler
{
	readonly HashSet<INotifyCollectionChanged> _itemCollections = new();
	readonly HashSet<IShellItemController> _itemControllers = new();
	Shell? _observedShell;
	DispatcherOperation? _itemsRefresh;

	void ConnectShellItems()
	{
		if (ReferenceEquals(_observedShell, VirtualView))
			return;

		DisconnectShellItems();
		_observedShell = VirtualView;
		if (_observedShell == null)
			return;

		UpdateItemCollectionSubscriptions();
	}

	void DisconnectShellItems()
	{
		_itemsRefresh?.Abort();
		_itemsRefresh = null;
		foreach (var collection in _itemCollections)
			collection.CollectionChanged -= OnItemsCollectionChanged;
		_itemCollections.Clear();
		foreach (var controller in _itemControllers)
			controller.ItemsCollectionChanged -= OnItemsCollectionChanged;
		_itemControllers.Clear();
		_observedShell = null;
	}

	void UpdateItemCollectionSubscriptions()
	{
		var collections = new HashSet<INotifyCollectionChanged>();
		var controllers = new HashSet<IShellItemController>();
		if (_observedShell != null)
		{
			collections.Add((INotifyCollectionChanged)_observedShell.Items);
			foreach (var item in _observedShell.Items)
			{
				controllers.Add(item);
				collections.Add((INotifyCollectionChanged)item.Items);
				foreach (var section in item.Items)
					collections.Add((INotifyCollectionChanged)section.Items);
			}
		}

		foreach (var removed in _itemCollections.Except(collections))
			removed.CollectionChanged -= OnItemsCollectionChanged;
		foreach (var added in collections.Except(_itemCollections))
			added.CollectionChanged += OnItemsCollectionChanged;
		_itemCollections.Clear();
		_itemCollections.UnionWith(collections);

		foreach (var removed in _itemControllers.Except(controllers))
			removed.ItemsCollectionChanged -= OnItemsCollectionChanged;
		foreach (var added in controllers.Except(_itemControllers))
			added.ItemsCollectionChanged += OnItemsCollectionChanged;
		_itemControllers.Clear();
		_itemControllers.UnionWith(controllers);
	}

	void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		UpdateItemCollectionSubscriptions();
		if (_itemsRefresh != null || _observedShell == null)
			return;

		// Shell finishes normalizing CurrentItem after collection notifications.
		// Defer rendering to that settled state and coalesce Clear/Add bursts.
		_itemsRefresh = PlatformView.Dispatcher.InvokeAsync(() =>
		{
			_itemsRefresh = null;
			if (_observedShell != null)
				UpdateValue(nameof(Shell.Items));
		}, DispatcherPriority.Background);
	}
}
