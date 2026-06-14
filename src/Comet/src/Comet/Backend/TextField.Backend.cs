#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission + input write-back for TextField.
	public partial class TextField
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			var text = Text?.CurrentValue;
			if (text is not null)
				node.ApplyProperty(PropertyIds.TextField_Text, PropertyValue.From(text));

			var placeholder = Placeholder?.CurrentValue;
			if (placeholder is not null)
				node.ApplyProperty(PropertyIds.TextField_Placeholder, PropertyValue.From(placeholder));

			if (this.GetEnvironment<Microsoft.Maui.Graphics.Color?>(EnvironmentKeys.Colors.Color) is { } color)
				node.ApplyProperty(PropertyIds.TextField_TextColor, PropertyValue.From(color));

			if (this.GetEnvironment<bool?>(this, "Comet.TextFieldBorderless", false) == true)
				node.ApplyProperty(PropertyIds.TextField_Borderless, PropertyValue.From(true));
		}

		/// <summary>Renders this field with no Material container or indicator line (a foundation
		/// <c>BasicTextField</c>) so it blends into its surroundings — e.g. a chat composer.</summary>
		public TextField Borderless()
		{
			this.SetEnvironment("Comet.TextFieldBorderless", true, false);
			return this;
		}

		protected internal override void OnBackendEvent<T>(Backend.EventId id, T payload)
		{
			// User edited the field. Optimistically reflect the new text on this control's own
			// node (the TextField is a controlled component), then write back through the
			// (possibly two-way) Text subscription so a bound Signal updates and dependents
			// re-render. See Toggle.OnBackendEvent for why the optimistic step is required.
			if (id == Backend.EventIds.TextChanged && payload is string s)
			{
				Node?.ApplyProperty(PropertyIds.TextField_Text, PropertyValue.From(s));
				Text?.Set(s);
			}
		}
	}
}
