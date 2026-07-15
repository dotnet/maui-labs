#nullable enable
#if IOS
using Comet.Backend;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;

namespace Comet.Platform.SwiftUI
{
	/// <summary>The iOS twin of <see cref="ComposeFilterChipNode"/>: Comet
	/// <see cref="Comet.FilterChip"/> hand-composed to the M3 chip metrics (32dp pill,
	/// selected = filled + check, unselected = outlined) — Android drives the real
	/// <c>FilterChip</c>, which self-themes; the twin takes the M3 defaults.</summary>
	sealed class SwiftUIFilterChipNode : SwiftUIHostedCompositionNode
	{
		const float HeightDp = 32f;

		FilterChip _control;
		bool _selected;
		bool _applied;

		public SwiftUIFilterChipNode(FilterChip control, BackendContext context)
			: base(context)
		{
			_control = control;
			_selected = control.IsSelected;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is FilterChip control)
			{
				_control = control;
				_selected = control.IsSelected;
			}
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			var label = CometBackendLayoutEngine.Measure(_control.LabelView);
			double check = _selected ? 26 : 0;
			return new(label.Width + 32 + check, HeightDp);
		}

		protected override View BuildContent()
		{
			var row = new HStack(spacing: 8f);
			if (_selected)
				row.Add(new Icon("check").IconSize(18)
					.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0));
			else if (_control.LeadingIconView is { } leading)
				row.Add(leading.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0));
			row.Add(_control.LabelView.VerticalLayoutAlignment(LayoutAlignment.Center));

			var pill = row
				.Frame(height: HeightDp)
				.Padding(new Thickness(16, 0, 16, 0))
				.CornerRadius(8)
				.OnTap(_ => _control.OnClick());
			return _selected
				? pill
				: pill.Border(1, Microsoft.Maui.Graphics.Colors.Gray);
		}

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.Toggle_IsOn)
			{
				base.ApplyProperty(id, in value);
				return;
			}
			if (_applied && value.AsBool == _selected)
				return;
			_applied = true;
			_selected = value.AsBool;
		}
	}
}
#endif
