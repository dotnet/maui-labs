using Microsoft.Maui.Controls;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Attached properties for configuring Shell behavior on macOS.
/// </summary>
public static class MacOSShell
{
	/// <summary>
	/// When true, Shell uses a native NSOutlineView source list sidebar instead of custom views.
	/// </summary>
	public static readonly BindableProperty UseNativeSidebarProperty =
		BindableProperty.CreateAttached(
			"UseNativeSidebar",
			typeof(bool),
			typeof(MacOSShell),
			false);

	public static bool GetUseNativeSidebar(BindableObject obj)
		=> (bool)obj.GetValue(UseNativeSidebarProperty);

	public static void SetUseNativeSidebar(BindableObject obj, bool value)
		=> obj.SetValue(UseNativeSidebarProperty, value);

	/// <summary>
	/// SF Symbol name for a Shell item's sidebar icon (e.g. "house.fill", "gear").
	/// Can be set on ShellContent, ShellSection, or FlyoutItem.
	/// </summary>
	public static readonly BindableProperty SystemImageProperty =
		BindableProperty.CreateAttached(
			"SystemImage",
			typeof(string),
			typeof(MacOSShell),
			null);

	public static string? GetSystemImage(BindableObject obj)
		=> (string?)obj.GetValue(SystemImageProperty);

	public static void SetSystemImage(BindableObject obj, string? value)
		=> obj.SetValue(SystemImageProperty, value);

	/// <summary>
	/// When true, the sidebar can be resized by dragging the divider between
	/// the sidebar and content area. Defaults to true.
	/// </summary>
	public static readonly BindableProperty IsSidebarResizableProperty =
		BindableProperty.CreateAttached(
			"IsSidebarResizable",
			typeof(bool),
			typeof(MacOSShell),
			true);

	public static bool GetIsSidebarResizable(BindableObject obj)
		=> (bool)obj.GetValue(IsSidebarResizableProperty);

	public static void SetIsSidebarResizable(BindableObject obj, bool value)
		=> obj.SetValue(IsSidebarResizableProperty, value);
}
