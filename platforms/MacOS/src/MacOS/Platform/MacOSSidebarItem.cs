using Microsoft.Maui.Controls;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Represents an item in a native macOS sidebar (NSOutlineView source list).
/// Items with <see cref="Children"/> are rendered as section headers (group rows).
/// Leaf items are rendered as selectable rows with icon + text.
/// </summary>
public class MacOSSidebarItem
{
	/// <summary>
	/// Display title for the sidebar item.
	/// </summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>
	/// SF Symbol name (e.g. "house.fill", "gear", "star").
	/// Takes priority over <see cref="Icon"/> when set.
	/// </summary>
	public string? SystemImage { get; set; }

	/// <summary>
	/// MAUI ImageSource fallback when <see cref="SystemImage"/> is not set.
	/// </summary>
	public ImageSource? Icon { get; set; }

	/// <summary>
	/// Child items. When set, this item becomes a section header (group row)
	/// and is not itself selectable.
	/// </summary>
	public IList<MacOSSidebarItem>? Children { get; set; }

	/// <summary>
	/// Developer-defined tag for identifying which item was selected.
	/// </summary>
	public object? Tag { get; set; }

	/// <summary>
	/// Whether this item is a section header (has children).
	/// </summary>
	public bool IsGroup => Children != null && Children.Count > 0;
}
