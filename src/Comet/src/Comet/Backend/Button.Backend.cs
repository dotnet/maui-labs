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
		}
	}
}
