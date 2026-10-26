#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Renders Comet <see cref="Comet.FilterChip"/> as the REAL Material 3
	/// <c>FilterChip</c> (the Jetcaster category tabs): selected state + label/leading
	/// slots; the widget self-themes fill/outline/check.</summary>
	// IBackendManagesOwnContent: the slots are materialized by THIS node into the
	// widget's lambdas — without it the bridge also auto-materializes the children.
	sealed class ComposeFilterChipNode : ComposeNode, IBackendManagesOwnContent
	{
		const float HeightDp = 32f;   // M3 chip container height

		FilterChip _control;
		readonly BackendContext _context;
		readonly MutableState<bool> _selected = new(false);
		ComposeNode? _labelNode;
		ComposeNode? _leadingNode;

		public ComposeFilterChipNode(FilterChip control, BackendContext context)
		{
			_control = control;
			_context = context;
			_selected.Value = control.IsSelected;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not FilterChip control)
				return;
			_control = control;
			_selected.Value = control.IsSelected;
			_labelNode = null;   // slot views were rebuilt with the owner
			_leadingNode = null;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Toggle_IsOn)
				_selected.Value = value.AsBool;
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			// The label view only measures once its backend node exists — materialize the
			// slot first (idempotent; Render reuses the same node).
			_labelNode ??= (ComposeNode)CometBackendBridge.Materialize(_control.LabelView, _context, _control);
			// Label width + M3 chip paddings (16dp label-only sides, +check room selected).
			var label = CometBackendLayoutEngine.Measure(_control.LabelView);
			double lead = _control.LeadingIconView is null ? 0 : 26;
			double check = _selected.Value ? 26 : 0;
			return new(label.Width + 32 + lead + check, HeightDp);
		}

		public override void Render(IComposer composer)
		{
			_labelNode ??= (ComposeNode)CometBackendBridge.Materialize(_control.LabelView, _context, _control);
			if (_control.LeadingIconView is { } leading)
				_leadingNode ??= (ComposeNode)CometBackendBridge.Materialize(leading, _context, _control);

			var chip = new AndroidX.Compose.FilterChip(_selected.Value, () => _control.OnClick())
			{
				Label = _labelNode,
				LeadingIcon = _leadingNode,
			};
			((ComposableNode)chip).Modifier = BuildNodeModifier();
			chip.Render(composer);
		}
	}
}
#endif
