#nullable enable
#if IOS
using Comet.Backend;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;

namespace Comet.Platform.SwiftUI
{
	/// <summary>The iOS twin of <see cref="ComposeIconToggleNode"/>: Comet
	/// <see cref="Comet.IconToggleButton"/> hand-composed to the M3 metrics (48dp tap
	/// target, centered icon slot) — Android drives the real <c>IconToggleButton</c>.</summary>
	sealed class SwiftUIIconToggleNode : SwiftUIHostedCompositionNode
	{
		const float TargetDp = 48f;

		IconToggleButton _control;
		bool _checked;
		bool _applied;

		public SwiftUIIconToggleNode(IconToggleButton control, BackendContext context)
			: base(context)
		{
			_control = control;
			_checked = control.IsChecked;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is IconToggleButton control)
			{
				_control = control;
				_checked = control.IsChecked;
			}
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
			=> new(TargetDp, TargetDp);

		protected override View BuildContent() => new HStack(spacing: 0f)
		{
			new HStack().FlexGrow(1),
			_control.IconView.VerticalLayoutAlignment(LayoutAlignment.Center),
			new HStack().FlexGrow(1),
		}
		.Frame(width: TargetDp, height: TargetDp)
		.OnTap(_ => _control.OnChange(!_checked));

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.Toggle_IsOn)
			{
				base.ApplyProperty(id, in value);
				return;
			}
			if (_applied && value.AsBool == _checked)
				return;
			_applied = true;
			_checked = value.AsBool;
		}
	}
}
#endif
