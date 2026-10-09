#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission + dismiss write-back for Drawer.
	public partial class Drawer
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			// Push the open state, and (once) forward future signal changes to the node so a nav
			// button toggling IsOpen slides the drawer without a full re-render.
			node.ApplyProperty(PropertyIds.Drawer_IsOpen, PropertyValue.From(IsOpen.Peek()));

			if (!_hooked)
			{
				_hooked = true;
				IsOpen.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.Drawer_IsOpen, PropertyValue.From(IsOpen.Peek()));
			}
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// Gesture open/dismiss (edge-swipe, scrim tap) — reflect back into the signal.
			if (id == Backend.EventIds.DrawerClosed)
				IsOpen.Value = false;
			else if (id == Backend.EventIds.DrawerOpened)
				IsOpen.Value = true;
		}
	}
}
