using System;
using AppKit;
using Microsoft.Maui;

namespace Comet.MacOS
{
	/// <summary>
	/// Extension methods for converting IView to NSView on macOS.
	/// This is Comet's local equivalent of Platform.Maui.MacOS.ViewExtensions.
	/// When Platform.Maui.MacOS is added as a dependency, these can delegate
	/// to or be replaced by the upstream implementations.
	/// </summary>
	public static class MacOSViewExtensions
	{
		public static NSView ToMacOSPlatform(this IView view, IMauiContext context)
		{
			var handler = view.ToHandler(context);
			if (handler.PlatformView is NSView nsView)
				return nsView;

			throw new InvalidOperationException(
				$"Unable to convert handler platform view ({handler.PlatformView?.GetType().Name}) to NSView");
		}
	}
}
