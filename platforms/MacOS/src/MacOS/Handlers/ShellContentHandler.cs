using AppKit;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Minimal ShellContent handler for macOS. Required for Shell's internal navigation
/// system (GoToAsync) to work. Creates the content page via IShellContentController
/// so that Shell.CurrentPage is non-null (required for navigation to complete).
/// </summary>
public partial class ShellContentHandler : ElementHandler<ShellContent, NSView>
{
	public static readonly IPropertyMapper<ShellContent, ShellContentHandler> Mapper =
		new PropertyMapper<ShellContent, ShellContentHandler>(ElementMapper)
		{
		};

	public ShellContentHandler() : base(Mapper) { }

	protected override NSView CreatePlatformElement()
	{
		return new NSView();
	}

	protected override void ConnectHandler(NSView platformElement)
	{
		base.ConnectHandler(platformElement);

		// Only eagerly create content for the currently active ShellContent.
		// Creating all pages upfront triggers PlatformBehavior.OnLoaded on pages
		// that aren't visible yet (can crash) and wastes resources.
		// Non-current content pages are created lazily by ShowCurrentPage().
		if (VirtualView is IShellContentController controller)
		{
			var shell = VirtualView?.Parent?.Parent?.Parent as Shell;
			var isCurrentContent = shell?.CurrentItem?.CurrentItem?.CurrentItem == VirtualView;
			if (isCurrentContent)
			{
				controller.GetOrCreateContent();
			}
		}
	}
}
