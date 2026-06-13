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
		readonly MutableState<int> _spacing = new(0);

		public ComposeStackNode(StackAxis axis) => _axis = axis;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Stack_Spacing)
				_spacing.Value = (int)value.AsDouble;
		}

		public override void Render(IComposer composer)
		{
			int spacing = _spacing.Value;
			var arrangement = spacing > 0 ? Arrangement.SpacedBy(spacing) : null;

			ComposableContainer container = _axis switch
			{
				StackAxis.Horizontal => new Row(arrangement),
				StackAxis.Depth => new Box(),
				_ => new Column(arrangement),
			};
			// background / padding / tap gesture become the stack's modifier chain.
			((ComposableNode)container).Modifier = BuildNodeModifier();
			AddChildrenTo(container);
			((ComposableNode)container).Render(composer);
		}
	}
}
#endif
