#nullable enable
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A Material 3 primary tab row (Compose <c>PrimaryTabRow</c> + <c>Tab</c>) — the
	/// JetNews Interests switcher. Text-label tabs; the signal drives the selected
	/// index (and the sliding indicator) reactively. Styling tokens ride on the
	/// control so the Android node styles the real widgets and an iOS twin can
	/// hand-compose identically.
	/// </summary>
	public partial class TabBar : View
	{
		public TabBar(Signal<int> selectedIndex, IReadOnlyList<string> titles,
			Color? selectedColor = null, Color? unselectedColor = null,
			string? fontFamily = null, double fontSize = 16, int fontWeight = 500)
		{
			SelectedIndex = selectedIndex;
			Titles = titles;
			SelectedColor = selectedColor;
			UnselectedColor = unselectedColor;
			FontFamily = fontFamily;
			FontSize = fontSize;
			FontWeight = fontWeight;
		}

		public Signal<int> SelectedIndex { get; }
		public IReadOnlyList<string> Titles { get; }
		public Color? SelectedColor { get; }
		public Color? UnselectedColor { get; }
		public string? FontFamily { get; }
		public double FontSize { get; }
		public int FontWeight { get; }

		public void SelectItem(int index) => SelectedIndex.Value = index;
	}
}
