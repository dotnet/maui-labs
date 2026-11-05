#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission for FormattedText (the runs + the base font, reusing Text_*).
	public partial class FormattedText
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			node.ApplyProperty(PropertyIds.Text_Runs, PropertyValue.FromObject(Runs));

			if (this.GetEnvironment<double?>(EnvironmentKeys.Fonts.Size) is { } fontSize)
				node.ApplyProperty(PropertyIds.Text_FontSize, PropertyValue.From(fontSize));

			if (this.GetEnvironment<double?>(EnvironmentKeys.Fonts.LineHeight) is { } lineHeight)
				node.ApplyProperty(PropertyIds.Text_LineHeight, PropertyValue.From(lineHeight));

			if (this.GetEnvironment<TextLineBreak?>(EnvironmentKeys.Text.LineBreak) is { } lineBreak and not TextLineBreak.Default)
				node.ApplyProperty(PropertyIds.Text_LineBreak, PropertyValue.From((int)lineBreak));

			if (this.GetEnvironment<string>(EnvironmentKeys.Fonts.Family) is { Length: > 0 } family)
				node.ApplyProperty(PropertyIds.Text_FontFamily, PropertyValue.From(family));
		}
	}
}
