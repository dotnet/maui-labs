#nullable enable
#if IOS
using Comet.Backend;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;

namespace Comet.Platform.SwiftUI
{
	/// <summary>The iOS twin of <see cref="ComposeTabRowNode"/>: Comet <see cref="Comet.TabBar"/>
	/// hand-composed to the M3 primary-tab-row metrics (48dp row, equal-width text tabs,
	/// 3dp content-width indicator under the selected tab) from the control's styling
	/// tokens — Android drives the real <c>PrimaryTabRow</c>.</summary>
	sealed class SwiftUITabRowNode : SwiftUIHostedCompositionNode
	{
		const float RowHeight = 48f;

		TabBar _bar;
		int _selected;
		bool _applied;

		public SwiftUITabRowNode(TabBar bar, BackendContext context)
			: base(context) => _bar = bar;

		public void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is TabBar bar)
				_bar = bar;
		}

		// In-flow footprint: a fixed 48dp row (the base's fill size would starve siblings).
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : ScreenDp().Width;
			return new Size(w, RowHeight);
		}

		protected override View BuildContent()
		{
			var row = new HStack(spacing: 0f);
			for (int i = 0; i < _bar.Titles.Count; i++)
			{
				int index = i;
				var color = index == _selected
					? _bar.SelectedColor ?? Microsoft.Maui.Graphics.Colors.Black
					: _bar.UnselectedColor ?? Microsoft.Maui.Graphics.Colors.Gray;
				var label = new Text(_bar.Titles[i])
					.FontSize((float)_bar.FontSize)
					.FontWeight((FontWeight)_bar.FontWeight)
					.Color(color);
				if (_bar.FontFamily is { } family)
					label = label.FontFamily(family);

				// The label + a content-width indicator stack (the column shrinks to the
				// label, so the 3dp bar underneath matches the text width — M3's primary
				// indicator), centered in an equal-flex cell.
				var cell = new VStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					label.HorizontalLayoutAlignment(LayoutAlignment.Center),
					new HStack().FlexGrow(1),
					new HStack().Frame(height: 3)
						.Background(index == _selected ? _bar.SelectedColor : null)
						.CornerRadius(1.5f)
						.HorizontalLayoutAlignment(LayoutAlignment.Fill),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Center);

				row.Add(new VStack(spacing: 0f) { cell }
					.FlexGrow(1).FlexBasis(0)
					.OnTap(_ => _bar.SelectItem(index)));
			}
			return row.Frame(height: RowHeight)
				.HorizontalLayoutAlignment(LayoutAlignment.Fill);
		}

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.Nav_SelectedIndex)
			{
				base.ApplyProperty(id, in value);
				return;
			}
			if (_applied && value.AsInt == _selected)
				return;
			_applied = true;
			_selected = value.AsInt;
			Refresh();
		}
	}
}
#endif
