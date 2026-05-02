using System;

namespace Comet
{
	/// <summary>
	/// Represents a toolbar item in the navigation bar.
	/// Matches MAUI's ToolbarItem for primary/secondary actions.
	/// </summary>
	public class ToolbarItem
	{
		public string Text { get; set; }
		public string IconGlyph { get; set; }
		public string IconFontFamily { get; set; }
		public Action OnClicked { get; set; }
		public ToolbarItemOrder Order { get; set; } = ToolbarItemOrder.Primary;
		public int Priority { get; set; }
		public bool IsEnabled { get; set; } = true;

		public ToolbarItem() { }

		public ToolbarItem(string text, Action onClicked)
		{
			Text = text;
			OnClicked = onClicked;
		}

		public ToolbarItem(string iconGlyph, string fontFamily, Action onClicked)
		{
			IconGlyph = iconGlyph;
			IconFontFamily = fontFamily;
			OnClicked = onClicked;
		}
	}

	public enum ToolbarItemOrder
	{
		/// <summary>Shows in the primary area (visible in the toolbar).</summary>
		Primary,
		/// <summary>Shows in the secondary area (overflow menu).</summary>
		Secondary,
		/// <summary>Default behavior (platform-dependent).</summary>
		Default
	}
}
