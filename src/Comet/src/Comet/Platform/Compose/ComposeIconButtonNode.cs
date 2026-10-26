#nullable enable
#if ANDROID
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Renders Comet <see cref="Comet.IconButton"/> as the REAL Material 3
	/// <c>IconButton</c>: 48dp state-layer target, bounded ripple, button semantics —
	/// the widget behind the gold's icon actions (Jetcaster queue-add/overflow).</summary>
	// IBackendManagesOwnContent: the icon slot is materialized by THIS node into the
	// widget's content lambda (same contract as ComposeIconToggleNode).
	sealed class ComposeIconButtonNode : ComposeNode, IBackendManagesOwnContent
	{
		const float TargetDp = 48f;   // M3 icon-button minimum touch target

		IconButton _control;
		readonly BackendContext _context;
		ComposeNode? _iconNode;

		public ComposeIconButtonNode(IconButton control, BackendContext context)
		{
			_control = control;
			_context = context;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not IconButton control)
				return;
			_control = control;
			_iconNode = null;   // the icon slot view was rebuilt with the owner
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			// No control-specific typed patches — the action rides the ctor callback.
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
			=> new(TargetDp, TargetDp);

		public override void Render(IComposer composer)
		{
			_iconNode ??= (ComposeNode)CometBackendBridge.Materialize(_control.IconView, _context, _control);
			var button = new AndroidX.Compose.IconButton(() => _control.OnClick())
			{
				_iconNode,
			};
			((AndroidX.Compose.ComposableNode)button).Modifier = BuildNodeModifier();
			button.Render(composer);
		}
	}
}
#endif
