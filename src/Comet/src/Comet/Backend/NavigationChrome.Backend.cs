#nullable enable
using System.ComponentModel;
using Comet.Backend;

namespace Comet
{
	// Backend property emission for the nav chrome controls: push the selected index and
	// (once) forward future signal changes so a selection re-highlights the platform items
	// without a full re-render. Mirrors Drawer.Backend.cs / SelectorPanel.Backend.cs.

	public partial class NavigationBar
	{
		PropertyChangedEventHandler? _selectedIndexChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (_selectedIndexChanged is null)
			{
				_selectedIndexChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
				SelectedIndex.PropertyChanged += _selectedIndexChanged;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _selectedIndexChanged is not null)
			{
				SelectedIndex.PropertyChanged -= _selectedIndexChanged;
				_selectedIndexChanged = null;
			}
			base.Dispose(disposing);
		}
	}

	public partial class NavigationRail
	{
		PropertyChangedEventHandler? _selectedIndexChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (_selectedIndexChanged is null)
			{
				_selectedIndexChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
				SelectedIndex.PropertyChanged += _selectedIndexChanged;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _selectedIndexChanged is not null)
			{
				SelectedIndex.PropertyChanged -= _selectedIndexChanged;
				_selectedIndexChanged = null;
			}
			base.Dispose(disposing);
		}
	}

	public partial class ContentSwitcher
	{
		PropertyChangedEventHandler? _indexChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.ContentSwitcher_Index, PropertyValue.From(Index.Peek()));
			if (_indexChanged is null)
			{
				_indexChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.ContentSwitcher_Index, PropertyValue.From(Index.Peek()));
				Index.PropertyChanged += _indexChanged;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _indexChanged is not null)
			{
				Index.PropertyChanged -= _indexChanged;
				_indexChanged = null;
			}
			base.Dispose(disposing);
		}
	}

	public partial class IconToggleButton
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Toggle_IsOn, PropertyValue.From(IsChecked));
		}
	}

	public partial class FilterChip
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Toggle_IsOn, PropertyValue.From(IsSelected));
		}
	}

	public partial class TabBar
	{
		PropertyChangedEventHandler? _selectedIndexChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (_selectedIndexChanged is null)
			{
				_selectedIndexChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
				SelectedIndex.PropertyChanged += _selectedIndexChanged;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _selectedIndexChanged is not null)
			{
				SelectedIndex.PropertyChanged -= _selectedIndexChanged;
				_selectedIndexChanged = null;
			}
			base.Dispose(disposing);
		}
	}

	public partial class NavigationSuite
	{
		PropertyChangedEventHandler? _selectedIndexChanged;
		PropertyChangedEventHandler? _drawerOpenChanged;
		INotifyPropertyChanged? _hookedDrawerOpen;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (DrawerOpen is { } drawer)
				node.ApplyProperty(PropertyIds.Drawer_IsOpen, PropertyValue.From(drawer.Peek()));
			if (_selectedIndexChanged is null)
			{
				_selectedIndexChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
				SelectedIndex.PropertyChanged += _selectedIndexChanged;
			}
			if (!ReferenceEquals(_hookedDrawerOpen, DrawerOpen))
			{
				DetachDrawerOpenSubscription();
				_hookedDrawerOpen = DrawerOpen;
				if (_hookedDrawerOpen is not null)
				{
					_drawerOpenChanged = (_, _) =>
						Node?.ApplyProperty(
							PropertyIds.Drawer_IsOpen,
							PropertyValue.From(DrawerOpen?.Peek() ?? false));
					_hookedDrawerOpen.PropertyChanged += _drawerOpenChanged;
				}
			}
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// Gesture open/dismiss (scrim tap / swipe / back) — reflect into the signal.
			if (id == Backend.EventIds.DrawerClosed && DrawerOpen is { } drawer)
				drawer.Value = false;
			else if (id == Backend.EventIds.DrawerOpened && DrawerOpen is { } opened)
				opened.Value = true;
		}

		void DetachDrawerOpenSubscription()
		{
			if (_hookedDrawerOpen is not null && _drawerOpenChanged is not null)
				_hookedDrawerOpen.PropertyChanged -= _drawerOpenChanged;
			_hookedDrawerOpen = null;
			_drawerOpenChanged = null;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_selectedIndexChanged is not null)
				{
					SelectedIndex.PropertyChanged -= _selectedIndexChanged;
					_selectedIndexChanged = null;
				}
				DetachDrawerOpenSubscription();
			}
			base.Dispose(disposing);
		}
	}
}
