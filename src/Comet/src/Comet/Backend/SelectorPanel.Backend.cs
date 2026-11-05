#nullable enable
using System.ComponentModel;
using Comet.Backend;

namespace Comet
{
	// Backend property emission + back-dismiss write-back for SelectorPanel.
	public partial class SelectorPanel
	{
		PropertyChangedEventHandler? _selectorChanged;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			// Push the active index, and (once) forward future signal changes so a selector tap swaps
			// the panel without a full re-render. The change also re-runs Yoga layout (AfterFlush),
			// growing/collapsing the footer as the panel's measured height changes.
			node.ApplyProperty(PropertyIds.SelectorPanel_Index, PropertyValue.From(Selector.Peek()));

			if (_selectorChanged is null)
			{
				_selectorChanged = (_, _) =>
				{
					Node?.ApplyProperty(PropertyIds.SelectorPanel_Index, PropertyValue.From(Selector.Peek()));
					// The panel's measured height changes with the index, so the layout must reflow (grow
					// the footer / shrink the message list). A bare signal set marks no view or effect
					// dirty, so it wouldn't otherwise schedule a flush — request one so the layout-driving
					// backend's AfterFlush hook re-runs Yoga layout.
					Comet.Reactive.ReactiveScheduler.EnsureFlushScheduled();
				};
				Selector.PropertyChanged += _selectorChanged;
			}
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// The node intercepted a system back press while a panel was open — collapse it.
			if (id == Backend.EventIds.SelectorPanelDismissed)
				Selector.Value = 0;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _selectorChanged is not null)
			{
				Selector.PropertyChanged -= _selectorChanged;
				_selectorChanged = null;
			}
			base.Dispose(disposing);
		}
	}
}
