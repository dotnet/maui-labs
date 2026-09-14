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

			if (this.GetEnvironment<double?>(EnvironmentKeys.Fonts.LineHeight) is { } lineHeight)
				node.ApplyProperty(PropertyIds.Text_LineHeight, PropertyValue.From(lineHeight));

			if (this.GetEnvironment<Microsoft.Maui.FontWeight?>(EnvironmentKeys.Fonts.Weight) is { } weight)
				node.ApplyProperty(PropertyIds.Text_FontWeight, PropertyValue.From((int)weight));

			if (this.GetEnvironment<string>(EnvironmentKeys.Fonts.Family) is { Length: > 0 } family)
				node.ApplyProperty(PropertyIds.Text_FontFamily, PropertyValue.From(family));

			if (this.GetEnvironment<int?>(EnvironmentKeys.Text.MaxLines) is { } maxLines and > 0)
				node.ApplyProperty(PropertyIds.Text_MaxLines, PropertyValue.From(maxLines));

			if (this.GetEnvironment<TextLineBreak?>(EnvironmentKeys.Text.LineBreak) is { } lineBreak and not TextLineBreak.Default)
				node.ApplyProperty(PropertyIds.Text_LineBreak, PropertyValue.From((int)lineBreak));

			if (this.GetEnvironment<Microsoft.Maui.FontSlant?>(EnvironmentKeys.Fonts.Slant) is Microsoft.Maui.FontSlant.Italic)
				node.ApplyProperty(PropertyIds.Text_Italic, PropertyValue.From(true));
		}
	}
}
