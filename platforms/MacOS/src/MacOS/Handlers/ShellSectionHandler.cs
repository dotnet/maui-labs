using AppKit;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Minimal ShellSection handler for macOS. Required for Shell's internal navigation
/// system (GoToAsync) to work. Must implement RequestNavigation command and call
/// NavigationFinished to complete the navigation pipeline.
/// </summary>
public partial class ShellSectionHandler : ElementHandler<ShellSection, NSView>
{
	public static readonly IPropertyMapper<ShellSection, ShellSectionHandler> Mapper =
		new PropertyMapper<ShellSection, ShellSectionHandler>(ElementMapper)
		{
			[nameof(ShellSection.CurrentItem)] = MapCurrentItem,
		};

	public static readonly CommandMapper<ShellSection, ShellSectionHandler> CommandMapper =
		new(ElementCommandMapper)
		{
			[nameof(IStackNavigation.RequestNavigation)] = RequestNavigation,
		};

	public ShellSectionHandler() : base(Mapper, CommandMapper) { }

	protected override NSView CreatePlatformElement()
	{
		return new NSView();
	}

	static void MapCurrentItem(ShellSectionHandler handler, ShellSection section)
	{
		// ShellHandler listens for Shell.CurrentItem changes and handles page switching
	}

	static void RequestNavigation(ShellSectionHandler handler, IStackNavigation view, object? arg)
	{
		if (arg is NavigationRequest request)
		{
			void Complete()
			{
				((IStackNavigation)view).NavigationFinished(request.NavigationStack);

				// Tell ShellHandler to show the correct page from the navigation stack
				if (handler.VirtualView is Element element)
				{
					var shell = element.FindParentOfType<Shell>();
					if (shell?.Handler is ShellHandler shellHandler)
						shellHandler.ShowCurrentPage();
				}
			}

			if (!NSThread.IsMain)
				NSApplication.SharedApplication.InvokeOnMainThread(Complete);
			else
				Complete();
		}
	}
}
