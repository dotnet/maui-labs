#nullable enable
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet
{
	// Backend property emission for Text. Merges with the generated `partial class Text`.
	// This is the hand-authored golden shape the source generator will emit per control.
	public partial class Text
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			var text = Value?.CurrentValue;
			if (text is not null)
				node.ApplyProperty(PropertyIds.Text_Value, PropertyValue.From(text));

			// TextColor is stored under the shared Color environment key (renamed at generation).
			if (this.GetEnvironment<Color?>(EnvironmentKeys.Colors.Color) is { } color)
				node.ApplyProperty(PropertyIds.Text_Color, PropertyValue.From(color));

			if (this.GetEnvironment<double?>(EnvironmentKeys.Fonts.Size) is { } fontSize)
				node.ApplyProperty(PropertyIds.Text_FontSize, PropertyValue.From(fontSize));
		}
	}
}
