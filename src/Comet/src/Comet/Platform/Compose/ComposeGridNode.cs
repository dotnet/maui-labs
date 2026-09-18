#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Hosts Grid children in a Box. The shared Comet layout engine computes
	/// every cell frame, including fixed/Auto/star tracks, spans, and spacing.</summary>
	sealed class ComposeGridNode : ComposeNode
	{
		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		public override void Render(IComposer composer)
		{
			var box = new Box { Modifier = BuildNodeModifier() };
			AddChildrenTo(box);
			box.Render(composer);
		}
	}
}
#endif
