#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Renders Comet <see cref="Comet.IconToggleButton"/> as the REAL Material 3
	/// <c>IconToggleButton</c> (the gold bookmark control): 48dp state-layer target, checked
	/// state + onCheckedChange routed back to the control's handler.</summary>
	sealed class ComposeIconToggleNode : ComposeNode
	{
		const float TargetDp = 48f;   // M3 icon-button minimum touch target

		IconToggleButton _control;
		readonly BackendContext _context;
		readonly MutableState<bool> _checked = new(false);
		ComposeNode? _iconNode;

		public ComposeIconToggleNode(IconToggleButton control, BackendContext context)
		{
			_control = control;
			_context = context;
			_checked.Value = control.IsChecked;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not IconToggleButton control)
				return;
			_control = control;
			_checked.Value = control.IsChecked;
			_iconNode = null;   // the icon slot view was rebuilt with the owner
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Toggle_IsOn)
				_checked.Value = value.AsBool;
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
			=> new(TargetDp, TargetDp);

		public override void Render(IComposer composer)
		{
			_iconNode ??= (ComposeNode)CometBackendBridge.Materialize(_control.IconView, _context, _control);
			var button = new AndroidX.Compose.IconToggleButton(_checked.Value, v => _control.OnChange(v))
			{
				_iconNode,
			};
			((ComposableNode)button).Modifier = BuildNodeModifier();
			button.Render(composer);
		}
	}
}
#endif
