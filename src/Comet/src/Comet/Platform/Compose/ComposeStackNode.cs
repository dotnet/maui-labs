#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Vertical / horizontal stack orientation for <see cref="ComposeStackNode"/>.</summary>
	enum StackAxis { Vertical, Horizontal, Depth }

	/// <summary>Renders Comet <c>VStack</c>/<c>HStack</c>/<c>ZStack</c> as a Compose
	/// <c>Column</c>/<c>Row</c>/<c>Box</c>, hosting the child nodes.</summary>
	sealed class ComposeStackNode : ComposeNode
	{
		readonly StackAxis _axis;

		public ComposeStackNode(StackAxis axis) => _axis = axis;

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			// Spacing/alignment wiring follows once Yoga drives positioning.
		}

		public override void Render(IComposer composer)
		{
			ComposableContainer container = _axis switch
			{
				StackAxis.Horizontal => new Row(),
				StackAxis.Depth => new Box(),
				_ => new Column(),
			};
			AddChildrenTo(container);
			((ComposableNode)container).Render(composer);
		}
	}
}
#endif
