#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission for the nav chrome controls: push the selected index and
	// (once) forward future signal changes so a selection re-highlights the platform items
	// without a full re-render. Mirrors Drawer.Backend.cs / SelectorPanel.Backend.cs.

	public partial class NavigationBar
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (!_hooked)
			{
				_hooked = true;
				SelectedIndex.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			}
		}
	}

	public partial class NavigationRail
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (!_hooked)
			{
				_hooked = true;
				SelectedIndex.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			}
		}
	}

	public partial class ContentSwitcher
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.ContentSwitcher_Index, PropertyValue.From(Index.Peek()));
			if (!_hooked)
			{
				_hooked = true;
				Index.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.ContentSwitcher_Index, PropertyValue.From(Index.Peek()));
			}
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
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (!_hooked)
			{
				_hooked = true;
				SelectedIndex.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			}
		}
	}

	public partial class NavigationSuite
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
			if (DrawerOpen is { } drawer)
				node.ApplyProperty(PropertyIds.Drawer_IsOpen, PropertyValue.From(drawer.Peek()));
			if (!_hooked)
			{
				_hooked = true;
				SelectedIndex.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedIndex.Peek()));
				if (DrawerOpen is { } d)
					d.PropertyChanged += (_, _) =>
						Node?.ApplyProperty(PropertyIds.Drawer_IsOpen, PropertyValue.From(d.Peek()));
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
	}
}
