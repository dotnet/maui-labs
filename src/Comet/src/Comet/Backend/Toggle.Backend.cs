#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission + input write-back for Toggle (ISwitch; IsOn -> Value).
	public partial class Toggle
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			if (Value is { } isOn)
				node.ApplyProperty(PropertyIds.Toggle_IsOn, PropertyValue.From(isOn.CurrentValue));
		}

		protected internal override void OnBackendEvent<T>(Backend.EventId id, T payload)
		{
			// User flipped the switch. Optimistically reflect the new value on this control's
			// own node (the Switch is a controlled component — its knob shows Node state), then
			// write back through the (possibly two-way) Value subscription so a bound Signal
			// updates and dependents re-render. The optimistic step is required because Set()
			// pre-updates the subscription's cached value, so the later flush sees "no change"
			// for this control and would otherwise leave its own node stale.
			if (id == Backend.EventIds.Toggled && payload is bool b)
			{
				Node?.ApplyProperty(PropertyIds.Toggle_IsOn, PropertyValue.From(b));
				Value?.Set(b);
			}
		}
	}
}
