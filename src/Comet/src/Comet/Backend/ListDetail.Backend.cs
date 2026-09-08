#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission + back-close write-back for ListDetail (the Drawer pattern).
	public partial class ListDetail
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.ListDetail_IsDetailOpen, PropertyValue.From(IsDetailOpen.Peek()));
			if (!_hooked)
			{
				_hooked = true;
				IsDetailOpen.PropertyChanged += (_, _) =>
					Node?.ApplyProperty(PropertyIds.ListDetail_IsDetailOpen, PropertyValue.From(IsDetailOpen.Peek()));
			}
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// Compact detail dismissed by the system back press — reflect into the signal.
			if (id == Backend.EventIds.DetailClosed)
				IsDetailOpen.Value = false;
		}
	}
}
