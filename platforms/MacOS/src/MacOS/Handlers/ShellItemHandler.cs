using AppKit;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Minimal ShellItem handler for macOS. Required for Shell's internal navigation
/// system (GoToAsync) to work. The main ShellHandler handles all visual rendering;
/// this handler just satisfies the handler resolution so Shell can set CurrentItem.
/// </summary>
public partial class ShellItemHandler : ElementHandler<ShellItem, NSView>
{
	public static readonly IPropertyMapper<ShellItem, ShellItemHandler> Mapper =
		new PropertyMapper<ShellItem, ShellItemHandler>(ElementMapper)
		{
			[nameof(ShellItem.CurrentItem)] = MapCurrentItem,
		};

	public ShellItemHandler() : base(Mapper) { }

	protected override NSView CreatePlatformElement()
	{
		// No platform view needed — ShellHandler manages all rendering
		return new NSView();
	}

	static void MapCurrentItem(ShellItemHandler handler, ShellItem item)
	{
		// ShellHandler listens for Shell.CurrentItem changes and handles page switching
	}
}
