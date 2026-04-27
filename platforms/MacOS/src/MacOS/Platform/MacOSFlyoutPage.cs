using Microsoft.Maui.Controls;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Attached properties for configuring a native macOS sidebar on FlyoutPage.
/// Used with <see cref="Microsoft.Maui.Platforms.MacOS.Handlers.NativeSidebarFlyoutPageHandler"/>.
/// </summary>
public static class MacOSFlyoutPage
{
	/// <summary>
	/// When true, FlyoutPage uses NSSplitViewController for the native inset sidebar
	/// appearance with behind-window vibrancy and traffic lights inside the sidebar.
	/// </summary>
	public static readonly BindableProperty UseNativeSidebarProperty =
		BindableProperty.CreateAttached(
			"UseNativeSidebar",
			typeof(bool),
			typeof(MacOSFlyoutPage),
			false);

	public static bool GetUseNativeSidebar(BindableObject obj)
		=> (bool)obj.GetValue(UseNativeSidebarProperty);

	public static void SetUseNativeSidebar(BindableObject obj, bool value)
		=> obj.SetValue(UseNativeSidebarProperty, value);

	/// <summary>
	/// Provides structured sidebar items for the native NSOutlineView source list.
	/// </summary>
	public static readonly BindableProperty SidebarItemsProperty =
		BindableProperty.CreateAttached(
			"SidebarItems",
			typeof(IList<MacOSSidebarItem>),
			typeof(MacOSFlyoutPage),
			null);

	public static IList<MacOSSidebarItem>? GetSidebarItems(BindableObject obj)
		=> (IList<MacOSSidebarItem>?)obj.GetValue(SidebarItemsProperty);

	public static void SetSidebarItems(BindableObject obj, IList<MacOSSidebarItem>? value)
		=> obj.SetValue(SidebarItemsProperty, value);

	/// <summary>
	/// Callback invoked when a sidebar item is selected.
	/// </summary>
	public static readonly BindableProperty SidebarSelectionChangedProperty =
		BindableProperty.CreateAttached(
			"SidebarSelectionChanged",
			typeof(Action<MacOSSidebarItem>),
			typeof(MacOSFlyoutPage),
			null);

	public static Action<MacOSSidebarItem>? GetSidebarSelectionChanged(BindableObject obj)
		=> (Action<MacOSSidebarItem>?)obj.GetValue(SidebarSelectionChangedProperty);

	public static void SetSidebarSelectionChanged(BindableObject obj, Action<MacOSSidebarItem>? value)
		=> obj.SetValue(SidebarSelectionChangedProperty, value);

	/// <summary>
	/// Gets or sets the currently selected sidebar item. Setting this programmatically
	/// updates the NSOutlineView selection without firing the SidebarSelectionChanged callback.
	/// </summary>
	public static readonly BindableProperty SelectedItemProperty =
		BindableProperty.CreateAttached(
			"SelectedItem",
			typeof(MacOSSidebarItem),
			typeof(MacOSFlyoutPage),
			null,
			propertyChanged: OnSelectedItemChanged);

	public static MacOSSidebarItem? GetSelectedItem(BindableObject obj)
		=> (MacOSSidebarItem?)obj.GetValue(SelectedItemProperty);

	public static void SetSelectedItem(BindableObject obj, MacOSSidebarItem? value)
		=> obj.SetValue(SelectedItemProperty, value);

	static void OnSelectedItemChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		if (bindable is not FlyoutPage flyoutPage || newValue is not MacOSSidebarItem item)
			return;

		if (flyoutPage.Handler is Handlers.NativeSidebarFlyoutPageHandler handler && !handler.IsProgrammaticSelection)
			handler.SelectItem(item, suppressCallback: true);
	}

	/// <summary>
	/// Selects a sidebar item matching the given predicate. The SidebarSelectionChanged
	/// callback is suppressed. Returns the matched item, or null if not found.
	/// </summary>
	public static MacOSSidebarItem? SelectSidebarItem(BindableObject obj, Func<MacOSSidebarItem, bool> predicate)
	{
		var items = GetSidebarItems(obj);
		if (items == null)
			return null;

		var match = FindItem(items, predicate);
		if (match != null)
			SetSelectedItem(obj, match);

		return match;
	}

	static MacOSSidebarItem? FindItem(IList<MacOSSidebarItem> items, Func<MacOSSidebarItem, bool> predicate)
	{
		foreach (var item in items)
		{
			if (!item.IsGroup && predicate(item))
				return item;

			if (item.Children != null)
			{
				var found = FindItem(item.Children, predicate);
				if (found != null)
					return found;
			}
		}
		return null;
	}
}
