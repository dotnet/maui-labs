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
				node.ApplyProperty(PropertyIds.Icon_Symbol, PropertyValue.From(Symbol));

			if (this.GetEnvironment<Color?>(EnvironmentKeys.Colors.Color) is { } tint)
				node.ApplyProperty(PropertyIds.Icon_Tint, PropertyValue.From(tint));

			if (this.GetEnvironment<double?>(this, "Comet.IconSize", false) is { } size && size > 0)
				node.ApplyProperty(PropertyIds.Icon_Size, PropertyValue.From(size));
		}
	}
}
