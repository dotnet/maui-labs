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

			// Emit the outlined flag whenever it was set (true or false) so a reactive toggle back to
			// filled reaches the node — the set-only patch would otherwise leave it stuck outlined.
			if (this.GetEnvironment<bool?>(this, "Comet.ButtonOutlined", false) is { } outlined)
				node.ApplyProperty(PropertyIds.Button_Outlined, PropertyValue.From(outlined));

			if (this.GetEnvironment<bool?>(this, "Comet.ButtonTextButton", false) == true)
				node.ApplyProperty(PropertyIds.Button_TextButton, PropertyValue.From(true));
		}

		/// <summary>Renders this button as a Material <c>TextButton</c> (no fill, no border — just the
		/// label in the content color), the gold standard's dialog/confirm button style.</summary>
		public Button TextButton()
		{
			this.SetEnvironment("Comet.ButtonTextButton", true, false);
			return this;
		}

		/// <summary>Renders this button as a Material <c>OutlinedButton</c> (a bordered, no-fill
		/// button) instead of the default filled style.</summary>
		public Button Outlined() => Outlined(true);

		/// <summary>Toggles the Material <c>OutlinedButton</c> (bordered, no-fill) vs the default
		/// filled style. Settable both ways so it can be flipped reactively (e.g. a Send button that
		/// is outlined while empty and fills once there's text).</summary>
		public Button Outlined(bool on)
		{
			this.SetEnvironment("Comet.ButtonOutlined", (object)on, false);
			return this;
		}

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			if (id == Backend.EventIds.Clicked)
				Clicked?.Invoke();
		}
	}
}
