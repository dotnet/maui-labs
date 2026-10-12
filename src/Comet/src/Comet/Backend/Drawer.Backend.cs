#nullable enable
using System.ComponentModel;
using Comet.Backend;

namespace Comet
{
	// Backend property emission + dismiss write-back for Drawer.
	public partial class Drawer
	{
		PropertyChangedEventHandler? _isOpenChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			// Push the open state, and (once) forward future signal changes to the node so a nav
			// button toggling IsOpen slides the drawer without a full re-render.
			node.ApplyProperty(PropertyIds.Drawer_IsOpen, PropertyValue.From(IsOpen.Peek()));

			if (_isOpenChanged is null)
			{
				_isOpenChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.Drawer_IsOpen, PropertyValue.From(IsOpen.Peek()));
				IsOpen.PropertyChanged += _isOpenChanged;
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

		protected override void Dispose(bool disposing)
		{
			if (disposing && _isOpenChanged is not null)
			{
				IsOpen.PropertyChanged -= _isOpenChanged;
				_isOpenChanged = null;
			}
			base.Dispose(disposing);
		}
	}
}
