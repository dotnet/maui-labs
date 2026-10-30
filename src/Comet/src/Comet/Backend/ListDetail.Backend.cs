#nullable enable
using System.ComponentModel;
using Comet.Backend;

namespace Comet
{
	// Backend property emission + back-close write-back for ListDetail (the Drawer pattern).
	public partial class ListDetail
	{
		PropertyChangedEventHandler? _detailOpenChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.ListDetail_IsDetailOpen, PropertyValue.From(IsDetailOpen.Peek()));
			if (_detailOpenChanged is null)
			{
				_detailOpenChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.ListDetail_IsDetailOpen, PropertyValue.From(IsDetailOpen.Peek()));
				IsDetailOpen.PropertyChanged += _detailOpenChanged;
			}
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// Compact detail dismissed by the system back press — reflect into the signal.
			if (id == Backend.EventIds.DetailClosed)
				IsDetailOpen.Value = false;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _detailOpenChanged is not null)
			{
				IsDetailOpen.PropertyChanged -= _detailOpenChanged;
				_detailOpenChanged = null;
			}
			base.Dispose(disposing);
		}
	}
}
