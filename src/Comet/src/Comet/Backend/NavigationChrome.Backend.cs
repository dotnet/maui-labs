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
}
