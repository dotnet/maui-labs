#nullable enable
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// A Material 3 icon toggle button (Compose <c>IconToggleButton</c>) — the gold's
	/// bookmark control: a 48dp tap target whose icon reflects a checked state; taps
	/// report the flipped value through <paramref name="onChange"/>. The icon slot is a
	/// view so the app supplies its checked/unchecked glyph (rows typically rebuild with
	/// the new state via ReloadData).
	/// </summary>
	public partial class IconToggleButton : View, IContainerView
	{
		public IconToggleButton(bool isChecked, System.Action<bool> onChange, View icon)
		{
			IsChecked = isChecked;
			OnChange = onChange;
			IconView = icon;
			icon.Parent = this;
		}

		public bool IsChecked { get; }
		public System.Action<bool> OnChange { get; }
		public View IconView { get; }

		public IReadOnlyList<View> GetChildren() => new[] { IconView };
	}
}
