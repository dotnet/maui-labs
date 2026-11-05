#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeText = AndroidX.Compose.Text;

namespace Comet.Platform.Compose
{
	/// <summary>Renders Comet <see cref="Comet.TabBar"/> as the REAL Material 3
	/// <c>PrimaryTabRow</c> with <c>Tab</c> children (text labels) — the gold
	/// Interests switcher. Selection flows signal → MutableState (indicator slides),
	/// and taps route back through <see cref="Comet.TabBar.SelectItem"/>.</summary>
	sealed class ComposeTabRowNode : ComposeNode
	{
		const float RowHeight = 48f;   // M3 text-tab container height

		TabBar _bar;
		readonly MutableState<int> _selected = new(0);

		public ComposeTabRowNode(TabBar bar) => _bar = bar;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Nav_SelectedIndex)
				_selected.Value = value.AsInt;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is TabBar bar)
				_bar = bar;
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
			=> new(double.IsInfinity(widthConstraint) ? ScreenSizeDp().Width : widthConstraint, RowHeight);

		public override void Render(IComposer composer)
		{
			int selected = _selected.Value;   // subscribe: selection recomposes the row
			var row = new PrimaryTabRow(selected) { Modifier = BuildNodeModifier() };
			for (int i = 0; i < _bar.Titles.Count; i++)
			{
				int index = i;
				var label = new ComposeText(_bar.Titles[i]);
				label.LetterSpacing = AndroidX.Compose.Sp.Zero;
				label.FontSize = new AndroidX.Compose.Sp((int)System.Math.Round(_bar.FontSize));
				var color = index == selected ? _bar.SelectedColor : _bar.UnselectedColor;
				if (color is { } c)
					label.Color = ToComposeColor(c);
				if (ComposeFontRegistry.Resolve(_bar.FontFamily, _bar.FontWeight) is { } r)
					label.FontFamily = r.Family;
				else if (_bar.FontWeight >= 500)
					label.FontWeight = AndroidX.Compose.FontWeight.Medium;
				row.Add(new Tab(index == selected, () => _bar.SelectItem(index)) { Text = label });
			}
			row.Render(composer);
		}
	}
}
#endif
