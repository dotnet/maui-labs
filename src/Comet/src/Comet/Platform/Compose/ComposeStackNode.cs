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
		readonly MutableState<bool> _asSurface = new(false);

		public ComposeStackNode(StackAxis axis) => _axis = axis;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Stack_Spacing)
				_spacing.Value = (int)value.AsDouble;
			else if (id == PropertyIds.Container_Surface)
				_asSurface.Value = value.AsBool;
		}

		public override void Render(IComposer composer)
		{
			int spacing = _spacing.Value;

			// Opt-in: a real Material Surface (color + shape) — the widget the gold standard draws
			// chat bubbles with. The Surface owns the fill + clip, so children sit inside it.
			if (_asSurface.Value && Background is { } bgColor)
			{
				var surface = new AndroidX.Compose.Surface
				{
					Color = ToComposeColor(bgColor),
					Shape = HasRoundedCorners ? CornerShape() : null,
					Modifier = BuildNodeModifier(),
				};
				AddChildrenTo(surface);
				((ComposableNode)surface).Render(composer);
				return;
			}

			// When Yoga has arranged this stack, render a Box so children position themselves
			// absolutely (each child carries its own offset+size); Yoga owns spacing/axis then.
			ComposableContainer container;
			if (HasFrame)
			{
				container = new Box();
			}
			else
			{
				var arrangement = spacing > 0 ? Arrangement.SpacedBy(spacing) : null;
				container = _axis switch
				{
					StackAxis.Horizontal => new Row(arrangement),
					StackAxis.Depth => new Box(),
					_ => new Column(arrangement),
				};
			}
			// background / padding / tap gesture (+ Yoga frame) become the stack's modifier chain.
			((ComposableNode)container).Modifier = BuildNodeModifier();
			AddChildrenTo(container);
			((ComposableNode)container).Render(composer);
		}
	}
}
#endif
