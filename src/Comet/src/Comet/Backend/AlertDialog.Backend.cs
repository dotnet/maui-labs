#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission + dismiss write-back for AlertDialog (mirrors Drawer.Backend).
	public partial class AlertDialog
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			// Push the open state, and (once) forward future signal changes to the node so toggling
			// IsOpen shows/dismisses the dialog without a full re-render.
			node.ApplyProperty(PropertyIds.Dialog_IsOpen, PropertyValue.From(IsOpen.Peek()));

			if (!_hooked)
			{
				_hooked = true;
				IsOpen.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.Dialog_IsOpen, PropertyValue.From(IsOpen.Peek()));
			}
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// Scrim tap / back press dismissed the dialog — reflect it back into the signal.
			if (id == Backend.EventIds.DialogDismissed)
				IsOpen.Value = false;
		}
	}
}
