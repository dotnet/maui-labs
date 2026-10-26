#nullable enable
#if IOS
using Comet.Backend;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;

namespace Comet.Platform.SwiftUI
{
	/// <summary>The iOS twin of <see cref="ComposeIconButtonNode"/>: Comet
	/// <see cref="Comet.IconButton"/> hand-composed to the M3 metrics (48dp tap
	/// target, centered icon slot) — Android drives the real <c>IconButton</c>.</summary>
	sealed class SwiftUIIconButtonNode : SwiftUIHostedCompositionNode
	{
		const float TargetDp = 48f;

		IconButton _control;

		public SwiftUIIconButtonNode(IconButton control, BackendContext context)
			: base(context)
		{
			_control = control;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is IconButton control)
			{
				_control = control;
				if (IsBuilt)
					Refresh();   // the icon slot view was rebuilt with the owner
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
		.OnTap(_ => _control.OnClick());
	}
}
#endif
