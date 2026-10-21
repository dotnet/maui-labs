#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission + drag write-back for Slider (ISlider, double value).
	public partial class Slider
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			if (Value is { } v)
				node.ApplyProperty(PropertyIds.Slider_Value, PropertyValue.From(v.CurrentValue));
			if (Minimum is { } min)
				node.ApplyProperty(PropertyIds.Slider_Minimum, PropertyValue.From(min.CurrentValue));
			if (Maximum is { } max)
				node.ApplyProperty(PropertyIds.Slider_Maximum, PropertyValue.From(max.CurrentValue));
		}

		protected internal override void OnBackendEvent<T>(Backend.EventId id, T payload)
		{
			// User dragged the slider. Optimistic self-update (controlled component), then
			// write the new value back through the (possibly two-way) Value subscription.
			if (id == Backend.EventIds.ValueChanged && payload is double d)
			{
				Node?.ApplyProperty(PropertyIds.Slider_Value, PropertyValue.From(d));
				Value?.Set(d);
			}
		}
	}
}
