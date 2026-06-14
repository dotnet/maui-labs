#nullable enable
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet
{
	// Backend property emission for Button. Merges with the generated `partial class Button`.
	public partial class Button
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			var text = Text?.CurrentValue;
			if (text is not null)
				node.ApplyProperty(PropertyIds.Button_Text, PropertyValue.From(text));

			if (this.GetEnvironment<Color?>(EnvironmentKeys.Colors.Color) is { } color)
				node.ApplyProperty(PropertyIds.Button_TextColor, PropertyValue.From(color));

			if (this.GetEnvironment<bool?>(this, "Comet.ButtonOutlined", false) == true)
				node.ApplyProperty(PropertyIds.Button_Outlined, PropertyValue.From(true));
		}

		/// <summary>Renders this button as a Material <c>OutlinedButton</c> (a bordered, no-fill
		/// button) instead of the default filled style.</summary>
		public Button Outlined()
		{
			this.SetEnvironment("Comet.ButtonOutlined", true, false);
			return this;
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			if (id == Backend.EventIds.Clicked)
				Clicked?.Invoke();
		}
	}
}
