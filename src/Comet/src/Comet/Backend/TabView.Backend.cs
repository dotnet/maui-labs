#nullable enable
using System.ComponentModel;
using Comet.Backend;

namespace Comet
{
	// Backend property emission for TabView: push selected index to the platform
	// node and forward future signal changes so tab selection re-highlights without
	// a full re-render. Mirrors NavigationBar.Backend.cs.
	public partial class TabView
	{
		PropertyChangedEventHandler? _selectedSignalChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			node.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedSignal.Peek()));
			if (_selectedSignalChanged is null)
			{
				_selectedSignalChanged = (_, _) =>
					Node?.ApplyProperty(PropertyIds.Nav_SelectedIndex, PropertyValue.From(SelectedSignal.Peek()));
				SelectedSignal.PropertyChanged += _selectedSignalChanged;
			}
		}

		void DetachSelectedSignalSubscription()
		{
			if (_selectedSignalChanged is null)
				return;
			SelectedSignal.PropertyChanged -= _selectedSignalChanged;
			_selectedSignalChanged = null;
		}
	}
}
