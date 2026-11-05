#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeSpacer = AndroidX.Compose.Spacer;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.Spacer"/> as the REAL Compose Foundation
	/// <c>Spacer</c> composable — an empty layout that takes its size from its Yoga-computed
	/// frame (via <see cref="ComposeNode.BuildNodeModifier"/>). The native Spacer participates
	/// in Row/Column flexible-space allocation correctly.</summary>
	sealed class ComposeSpacerNode : ComposeNode
	{
		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		public override void Render(IComposer composer)
		{
			new ComposeSpacer(BuildNodeModifier() ?? Modifier.Companion).Render(composer);
		}
	}
}
#endif
