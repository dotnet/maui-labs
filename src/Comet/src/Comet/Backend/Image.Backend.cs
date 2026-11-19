#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend property emission for Image. Merges with the `partial class Image`.
	public partial class Image
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			// The string source (a URL or resource name) is what the backends resolve; the
			// IImageSource path is a later step.
			var source = StringSource?.CurrentValue;
			if (!string.IsNullOrEmpty(source))
				node.ApplyProperty(PropertyIds.Image_Source, PropertyValue.From(source));

			if (this.GetEnvironment<Microsoft.Maui.Aspect?>("Aspect") is { } aspect)
				node.ApplyProperty(PropertyIds.Image_Aspect, PropertyValue.From((int)aspect));
		}
	}
}
