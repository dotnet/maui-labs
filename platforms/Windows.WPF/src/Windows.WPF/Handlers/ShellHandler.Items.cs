using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Handlers.WPF;

public partial class ShellHandler
{
	readonly HashSet<INotifyCollectionChanged> _itemCollections = new();
	readonly HashSet<IShellItemController> _itemControllers = new();
	readonly HashSet<ShellSection> _enabledSections = new();
	Shell? _observedShell;
	DispatcherOperation? _itemsRefresh;
	int _itemsGeneration;

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
		Interlocked.Increment(ref _itemsGeneration);
		_itemsRefresh?.Abort();
		_itemsRefresh = null;
		foreach (var collection in _itemCollections)
			collection.CollectionChanged -= OnItemsCollectionChanged;
		_itemCollections.Clear();
		foreach (var controller in _itemControllers)
			controller.ItemsCollectionChanged -= OnItemsCollectionChanged;
		_itemControllers.Clear();
		foreach (var section in _enabledSections)
			section.PropertyChanged -= OnSectionIsEnabledChanged;
		_enabledSections.Clear();
		_observedShell = null;
	}

	void UpdateItemCollectionSubscriptions()
	{
		var collections = new HashSet<INotifyCollectionChanged>();
		var controllers = new HashSet<IShellItemController>();
		var sections = new HashSet<ShellSection>();
		if (_observedShell != null)
		{
			collections.Add((INotifyCollectionChanged)_observedShell.Items);
			foreach (var item in _observedShell.Items)
			{
				controllers.Add(item);
				collections.Add((INotifyCollectionChanged)item.Items);
				foreach (var section in item.Items)
				{
					sections.Add(section);
					collections.Add((INotifyCollectionChanged)section.Items);
				}
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

		foreach (var removed in _enabledSections.Except(sections))
			removed.PropertyChanged -= OnSectionIsEnabledChanged;
		foreach (var added in sections.Except(_enabledSections))
			added.PropertyChanged += OnSectionIsEnabledChanged;
		_enabledSections.Clear();
		_enabledSections.UnionWith(sections);
	}

	void OnSectionIsEnabledChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not ShellSection section || e.PropertyName != nameof(ShellSection.IsEnabled))
			return;

		var generation = Volatile.Read(ref _itemsGeneration);
		var shell = _observedShell;
		var platformView = PlatformView;
		if (shell == null || platformView == null)
			return;

		void UpdateEnabled()
		{
			if (generation != Volatile.Read(ref _itemsGeneration) ||
				!ReferenceEquals(_observedShell, shell) || !ReferenceEquals(VirtualView, shell) ||
				!ReferenceEquals(PlatformView, platformView) || !_enabledSections.Contains(section))
				return;

			platformView.UpdateTabIsEnabled(shell, section);
		}

		if (platformView.Dispatcher.CheckAccess())
			UpdateEnabled();
		else
			platformView.Dispatcher.InvokeAsync(UpdateEnabled);
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
