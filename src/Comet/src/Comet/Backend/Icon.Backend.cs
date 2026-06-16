#nullable enable
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet
{
	// Backend property emission for Icon.
	public partial class Icon
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			if (!string.IsNullOrEmpty(Symbol))
			{
				// If an icon font is registered and maps this symbol, render the font glyph (same on
				// every backend); otherwise fall back to the platform-native icon set (SF Symbol / Material
				// ImageVector / bundled asset).
				if (IconFont.TryGlyph(Symbol, out var glyph))
				{
					node.ApplyProperty(PropertyIds.Icon_Glyph, PropertyValue.From(glyph));
					node.ApplyProperty(PropertyIds.Icon_FontFamily, PropertyValue.From(IconFont.Family!));
				}
				else
					node.ApplyProperty(PropertyIds.Icon_Symbol, PropertyValue.From(Symbol));
			}

			if (this.GetEnvironment<Color?>(EnvironmentKeys.Colors.Color) is { } tint)
				node.ApplyProperty(PropertyIds.Icon_Tint, PropertyValue.From(tint));

			if (this.GetEnvironment<double?>(this, "Comet.IconSize", false) is { } size && size > 0)
				node.ApplyProperty(PropertyIds.Icon_Size, PropertyValue.From(size));
		}
	}
}
