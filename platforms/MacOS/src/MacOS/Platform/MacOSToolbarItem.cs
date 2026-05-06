using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Controls where a toolbar item appears relative to the sidebar tracking separator.
/// Sidebar placements (Leading/Center/Trailing) are separated by flexible spaces
/// so items distribute across the sidebar titlebar area like:
/// <c>[Leading...] ←flex→ [Center...] ←flex→ [Trailing...]  |  [Content...]</c>
/// These are ignored when an explicit <see cref="MacOSToolbar.SidebarLayoutProperty"/> is set.
/// </summary>
public enum MacOSToolbarItemPlacement
{
	/// <summary>Standard content area placement (right of the sidebar divider).</summary>
	Content = 0,

	/// <summary>Sidebar titlebar, left-aligned (equivalent to <see cref="SidebarLeading"/>).</summary>
	Sidebar = 1,

	/// <summary>Sidebar titlebar, left-aligned.</summary>
	SidebarLeading = 1,

	/// <summary>Sidebar titlebar, centered between flexible spaces.</summary>
	SidebarCenter = 2,

	/// <summary>Sidebar titlebar, right-aligned (pushed right by a flexible space).</summary>
	SidebarTrailing = 3,
}

/// <summary>Visibility priority for toolbar items when space is limited.</summary>
public enum MacOSToolbarItemVisibilityPriority
{
	/// <summary>Standard priority (default).</summary>
	Standard = 0,
	/// <summary>Low priority — hidden first when space is tight.</summary>
	Low = -1000,
	/// <summary>High priority — kept visible longer.</summary>
	High = 1000,
	/// <summary>User priority — always visible.</summary>
	User = 2000,
}

/// <summary>
/// Attached properties for configuring macOS-specific toolbar item behavior.
/// </summary>
public static class MacOSToolbarItem
{
	/// <summary>
	/// Controls where this toolbar item appears: in the content toolbar area,
	/// or in the sidebar titlebar area (leading, center, or trailing).
	/// Ignored when <see cref="MacOSToolbar.SidebarLayoutProperty"/> is set on the page.
	/// </summary>
	public static readonly BindableProperty PlacementProperty =
		BindableProperty.CreateAttached(
			"Placement",
			typeof(MacOSToolbarItemPlacement),
			typeof(MacOSToolbarItem),
			MacOSToolbarItemPlacement.Content);

	public static MacOSToolbarItemPlacement GetPlacement(BindableObject obj)
		=> (MacOSToolbarItemPlacement)obj.GetValue(PlacementProperty);

	public static void SetPlacement(BindableObject obj, MacOSToolbarItemPlacement value)
		=> obj.SetValue(PlacementProperty, value);

	/// <summary>Whether the toolbar button shows a bordered/bezel appearance.</summary>
	public static readonly BindableProperty IsBorderedProperty =
		BindableProperty.CreateAttached("IsBordered", typeof(bool), typeof(MacOSToolbarItem), true);

	public static bool GetIsBordered(BindableObject obj) => (bool)obj.GetValue(IsBorderedProperty);
	public static void SetIsBordered(BindableObject obj, bool value) => obj.SetValue(IsBorderedProperty, value);

	/// <summary>Badge text/count displayed on the toolbar item icon.</summary>
	public static readonly BindableProperty BadgeProperty =
		BindableProperty.CreateAttached("Badge", typeof(string), typeof(MacOSToolbarItem), null);

	public static string? GetBadge(BindableObject obj) => (string?)obj.GetValue(BadgeProperty);
	public static void SetBadge(BindableObject obj, string? value) => obj.SetValue(BadgeProperty, value);

	/// <summary>Tint color for the toolbar button background.</summary>
	public static readonly BindableProperty BackgroundTintColorProperty =
		BindableProperty.CreateAttached("BackgroundTintColor", typeof(Color), typeof(MacOSToolbarItem), null);

	public static Color? GetBackgroundTintColor(BindableObject obj) => (Color?)obj.GetValue(BackgroundTintColorProperty);
	public static void SetBackgroundTintColor(BindableObject obj, Color? value) => obj.SetValue(BackgroundTintColorProperty, value);

	/// <summary>Tooltip text override (defaults to ToolbarItem.Text if not set).</summary>
	public static readonly BindableProperty ToolTipProperty =
		BindableProperty.CreateAttached("ToolTip", typeof(string), typeof(MacOSToolbarItem), null);

	public static string? GetToolTip(BindableObject obj) => (string?)obj.GetValue(ToolTipProperty);
	public static void SetToolTip(BindableObject obj, string? value) => obj.SetValue(ToolTipProperty, value);

	/// <summary>Visibility priority when toolbar space is limited.</summary>
	public static readonly BindableProperty VisibilityPriorityProperty =
		BindableProperty.CreateAttached("VisibilityPriority", typeof(MacOSToolbarItemVisibilityPriority),
			typeof(MacOSToolbarItem), MacOSToolbarItemVisibilityPriority.Standard);

	public static MacOSToolbarItemVisibilityPriority GetVisibilityPriority(BindableObject obj)
		=> (MacOSToolbarItemVisibilityPriority)obj.GetValue(VisibilityPriorityProperty);

	public static void SetVisibilityPriority(BindableObject obj, MacOSToolbarItemVisibilityPriority value)
		=> obj.SetValue(VisibilityPriorityProperty, value);

	/// <summary>
	/// Controls whether this toolbar item is visible. When <c>false</c>, the native
	/// <c>NSToolbarItem.IsHidden</c> property is set to hide the item without removing
	/// it from the toolbar — avoiding the flash/flicker caused by a full toolbar rebuild.
	/// Defaults to <c>true</c>.
	/// </summary>
	public static readonly BindableProperty IsVisibleProperty =
		BindableProperty.CreateAttached("IsVisible", typeof(bool), typeof(MacOSToolbarItem), true,
			propertyChanged: OnIsVisibleChanged);

	public static bool GetIsVisible(BindableObject obj) => (bool)obj.GetValue(IsVisibleProperty);
	public static void SetIsVisible(BindableObject obj, bool value) => obj.SetValue(IsVisibleProperty, value);

	/// <summary>
	/// Maps special toolbar items (menu, search, group, etc.) back to their owning Page
	/// so that <see cref="OnIsVisibleChanged"/> can trigger a refresh when visibility changes.
	/// </summary>
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BindableObject, WeakReference<Page>> _ownerPages = new();

	internal static void SetOwnerPage(BindableObject item, Page page)
		=> _ownerPages.AddOrUpdate(item, new WeakReference<Page>(page));

	static void OnIsVisibleChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		Page? page = null;
		if (bindable is ToolbarItem ti)
			page = ti.Parent as Page;
		else if (_ownerPages.TryGetValue(bindable, out var weakRef))
			weakRef.TryGetTarget(out page);

		if (page != null)
			MacOSToolbar.RequestRefresh(page);
	}
}

/// <summary>Identifies a built-in system toolbar item provided by AppKit.</summary>
public enum SystemItemKind
{
	ToggleSidebar,
	ToggleInspector,
	CloudSharing,
	Print,
	ShowColors,
	ShowFonts,
	WritingTools,
	InspectorTrackingSeparator,
}

/// <summary>A built-in system toolbar item (e.g., sidebar toggle, print, writing tools).</summary>
public sealed class SystemItemLayoutItem : MacOSToolbarLayoutItem
{
	public SystemItemKind Kind { get; }
	internal SystemItemLayoutItem(SystemItemKind kind) => Kind = kind;
}

/// <summary>
/// Describes a single element in an explicit sidebar toolbar layout.
/// Use the static factory members to build a layout array.
/// </summary>
/// <example>
/// <code>
/// MacOSToolbar.SetSidebarLayout(page, new[]
/// {
///     MacOSToolbarLayoutItem.Item(addBtn),
///     MacOSToolbarLayoutItem.FlexibleSpace,
///     MacOSToolbarLayoutItem.Item(filterBtn),
/// });
/// </code>
/// </example>
public abstract class MacOSToolbarLayoutItem
{
	/// <summary>A spring that pushes adjacent items apart.</summary>
	public static readonly MacOSToolbarLayoutItem FlexibleSpace = new SpacerLayoutItem(SpacerKind.Flexible);

	/// <summary>A fixed-width space between items.</summary>
	public static readonly MacOSToolbarLayoutItem Space = new SpacerLayoutItem(SpacerKind.Fixed);

	/// <summary>A thin vertical separator line.</summary>
	public static readonly MacOSToolbarLayoutItem Separator = new SpacerLayoutItem(SpacerKind.Separator);

	/// <summary>The page title label (centered by default). Use in content layouts to position the title.</summary>
	public static readonly MacOSToolbarLayoutItem Title = new TitleLayoutItem();

	// --- System toolbar items ---

	/// <summary>Standard sidebar toggle button (provided by AppKit).</summary>
	public static readonly MacOSToolbarLayoutItem ToggleSidebar = new SystemItemLayoutItem(SystemItemKind.ToggleSidebar);

	/// <summary>Inspector panel toggle button.</summary>
	public static readonly MacOSToolbarLayoutItem ToggleInspector = new SystemItemLayoutItem(SystemItemKind.ToggleInspector);

	/// <summary>iCloud sharing button.</summary>
	public static readonly MacOSToolbarLayoutItem CloudSharing = new SystemItemLayoutItem(SystemItemKind.CloudSharing);

	/// <summary>Standard print button.</summary>
	public static readonly MacOSToolbarLayoutItem Print = new SystemItemLayoutItem(SystemItemKind.Print);

	/// <summary>Opens the system color picker panel.</summary>
	public static readonly MacOSToolbarLayoutItem ShowColors = new SystemItemLayoutItem(SystemItemKind.ShowColors);

	/// <summary>Opens the system font panel.</summary>
	public static readonly MacOSToolbarLayoutItem ShowFonts = new SystemItemLayoutItem(SystemItemKind.ShowFonts);

	/// <summary>Apple Intelligence writing tools button.</summary>
	public static readonly MacOSToolbarLayoutItem WritingTools = new SystemItemLayoutItem(SystemItemKind.WritingTools);

	/// <summary>Tracking separator for an inspector panel (right side).</summary>
	public static readonly MacOSToolbarLayoutItem InspectorTrackingSeparator = new SystemItemLayoutItem(SystemItemKind.InspectorTrackingSeparator);

	/// <summary>References a <see cref="ToolbarItem"/> that must also be in the page's ToolbarItems collection.</summary>
	public static MacOSToolbarLayoutItem Item(ToolbarItem item) => new ToolbarItemLayoutRef(item);

	/// <summary>
	/// A native macOS search field that starts as a magnifying glass icon and expands
	/// into an inline search field when clicked.
	/// </summary>
	public static MacOSToolbarLayoutItem Search(MacOSSearchToolbarItem search) => new SearchLayoutRef(search);

	/// <summary>A dropdown menu button in the toolbar.</summary>
	public static MacOSToolbarLayoutItem Menu(MacOSMenuToolbarItem menu) => new MenuLayoutRef(menu);

	/// <summary>A segmented control / grouped items in the toolbar.</summary>
	public static MacOSToolbarLayoutItem Group(MacOSToolbarItemGroup group) => new GroupLayoutRef(group);

	/// <summary>The system share button in the toolbar.</summary>
	public static MacOSToolbarLayoutItem Share(MacOSShareToolbarItem share) => new ShareLayoutRef(share);

	/// <summary>A dropdown popup selector in the toolbar.</summary>
	public static MacOSToolbarLayoutItem PopUp(MacOSPopUpToolbarItem popup) => new PopUpLayoutRef(popup);
}

/// <summary>The kind of spacer in a toolbar layout.</summary>
public enum SpacerKind { Flexible, Fixed, Separator }

/// <summary>A spacer or separator in the toolbar layout.</summary>
public sealed class SpacerLayoutItem : MacOSToolbarLayoutItem
{
	public SpacerKind Kind { get; }
	internal SpacerLayoutItem(SpacerKind kind) => Kind = kind;
}

/// <summary>A reference to a <see cref="ToolbarItem"/> in the toolbar layout.</summary>
public sealed class ToolbarItemLayoutRef : MacOSToolbarLayoutItem
{
	public ToolbarItem ToolbarItem { get; }
	internal ToolbarItemLayoutRef(ToolbarItem item) => ToolbarItem = item;
}

/// <summary>The page title element in a content toolbar layout.</summary>
public sealed class TitleLayoutItem : MacOSToolbarLayoutItem
{
	internal TitleLayoutItem() { }
}

/// <summary>A reference to a <see cref="MacOSSearchToolbarItem"/> in the toolbar layout.</summary>
public sealed class SearchLayoutRef : MacOSToolbarLayoutItem
{
	public MacOSSearchToolbarItem SearchItem { get; }
	internal SearchLayoutRef(MacOSSearchToolbarItem item) => SearchItem = item;
}

/// <summary>A reference to a <see cref="MacOSMenuToolbarItem"/> in the toolbar layout.</summary>
public sealed class MenuLayoutRef : MacOSToolbarLayoutItem
{
	public MacOSMenuToolbarItem MenuItem { get; }
	internal MenuLayoutRef(MacOSMenuToolbarItem item) => MenuItem = item;
}

/// <summary>A reference to a <see cref="MacOSToolbarItemGroup"/> in the toolbar layout.</summary>
public sealed class GroupLayoutRef : MacOSToolbarLayoutItem
{
	public MacOSToolbarItemGroup Group { get; }
	internal GroupLayoutRef(MacOSToolbarItemGroup group) => Group = group;
}

/// <summary>A reference to a <see cref="MacOSShareToolbarItem"/> in the toolbar layout.</summary>
public sealed class ShareLayoutRef : MacOSToolbarLayoutItem
{
	public MacOSShareToolbarItem ShareItem { get; }
	internal ShareLayoutRef(MacOSShareToolbarItem item) => ShareItem = item;
}

/// <summary>A reference to a <see cref="MacOSPopUpToolbarItem"/> in the toolbar layout.</summary>
public sealed class PopUpLayoutRef : MacOSToolbarLayoutItem
{
	public MacOSPopUpToolbarItem PopUpItem { get; }
	internal PopUpLayoutRef(MacOSPopUpToolbarItem item) => PopUpItem = item;
}

/// <summary>
/// A native macOS search toolbar item that starts as a magnifying glass icon and
/// expands into an inline search field when clicked (like Apple Notes, Finder, Mail).
/// Use with <see cref="MacOSToolbarLayoutItem.Search"/> in explicit toolbar layouts,
/// or attach to a page with <see cref="MacOSToolbar.SearchItemProperty"/>.
/// </summary>
public class MacOSSearchToolbarItem : BindableObject
{
	/// <summary>Placeholder text shown in the search field when empty.</summary>
	public static readonly BindableProperty PlaceholderProperty =
		BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(MacOSSearchToolbarItem), "Search");

	public string Placeholder
	{
		get => (string)GetValue(PlaceholderProperty);
		set => SetValue(PlaceholderProperty, value);
	}

	/// <summary>The current search text.</summary>
	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(MacOSSearchToolbarItem), string.Empty,
			propertyChanged: (b, o, n) => ((MacOSSearchToolbarItem)b).OnTextChanged((string)o, (string)n));

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	/// <summary>The preferred width of the search field when expanded.</summary>
	public static readonly BindableProperty PreferredWidthProperty =
		BindableProperty.Create(nameof(PreferredWidth), typeof(double), typeof(MacOSSearchToolbarItem), 200.0);

	public double PreferredWidth
	{
		get => (double)GetValue(PreferredWidthProperty);
		set => SetValue(PreferredWidthProperty, value);
	}

	/// <summary>
	/// Whether pressing Escape while the search field has focus collapses it back to an icon.
	/// Defaults to <c>true</c>.
	/// </summary>
	public static readonly BindableProperty ResignsFirstResponderWithCancelProperty =
		BindableProperty.Create(nameof(ResignsFirstResponderWithCancel), typeof(bool), typeof(MacOSSearchToolbarItem), true);

	public bool ResignsFirstResponderWithCancel
	{
		get => (bool)GetValue(ResignsFirstResponderWithCancelProperty);
		set => SetValue(ResignsFirstResponderWithCancelProperty, value);
	}

	/// <summary>
	/// Toolbar placement for this search item. Defaults to <see cref="MacOSToolbarItemPlacement.Content"/>.
	/// Also used when there is no explicit layout.
	/// </summary>
	public MacOSToolbarItemPlacement Placement { get; set; } = MacOSToolbarItemPlacement.Content;

	/// <summary>Fired when the search text changes.</summary>
	public event EventHandler<TextChangedEventArgs>? TextChanged;

	/// <summary>Fired when the user presses Return/Enter to commit a search.</summary>
	public event EventHandler<string>? SearchCommitted;

	/// <summary>Fired when the search field is opened (expanded).</summary>
	public event EventHandler? SearchStarted;

	/// <summary>Fired when the search field is dismissed (collapsed).</summary>
	public event EventHandler? SearchEnded;

	internal void RaiseSearchCommitted(string text) => SearchCommitted?.Invoke(this, text);
	internal void RaiseSearchStarted() => SearchStarted?.Invoke(this, EventArgs.Empty);
	internal void RaiseSearchEnded() => SearchEnded?.Invoke(this, EventArgs.Empty);

	void OnTextChanged(string oldValue, string newValue)
		=> TextChanged?.Invoke(this, new TextChangedEventArgs(oldValue, newValue));
}

// ── Menu Toolbar Item ──────────────────────────────────────────────────

/// <summary>A single item in a <see cref="MacOSMenuToolbarItem"/> dropdown menu.</summary>
public class MacOSMenuItem
{
	public string Text { get; set; } = string.Empty;

	/// <summary>SF Symbol icon name (e.g., "doc.text").</summary>
	public string? Icon { get; set; }

	public bool IsEnabled { get; set; } = true;

	/// <summary>Whether this item shows a checkmark.</summary>
	public bool IsChecked { get; set; }

	/// <summary>Keyboard shortcut (e.g., "s" for Cmd+S). Use with <see cref="KeyEquivalentModifiers"/>.</summary>
	public string? KeyEquivalent { get; set; }

	public ICommand? Command { get; set; }
	public object? CommandParameter { get; set; }

	/// <summary>Nested submenu items.</summary>
	public IList<MacOSMenuItem> SubItems { get; } = new List<MacOSMenuItem>();

	/// <summary>If true, renders as a separator line instead of a menu item.</summary>
	public bool IsSeparator { get; set; }

	public event EventHandler? Clicked;
	internal void RaiseClicked() => Clicked?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A toolbar button that opens a dropdown menu when clicked
/// (like sort/filter menus in Finder, Mail).
/// Wraps <c>NSMenuToolbarItem</c>.
/// </summary>
public class MacOSMenuToolbarItem : BindableObject
{
	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(MacOSMenuToolbarItem), string.Empty);
	public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

	/// <summary>SF Symbol icon name.</summary>
	public static readonly BindableProperty IconProperty =
		BindableProperty.Create(nameof(Icon), typeof(string), typeof(MacOSMenuToolbarItem), null);
	public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

	/// <summary>Whether to show the dropdown chevron indicator (default: true).</summary>
	public static readonly BindableProperty ShowsIndicatorProperty =
		BindableProperty.Create(nameof(ShowsIndicator), typeof(bool), typeof(MacOSMenuToolbarItem), true);
	public bool ShowsIndicator { get => (bool)GetValue(ShowsIndicatorProperty); set => SetValue(ShowsIndicatorProperty, value); }

	/// <summary>
	/// When true, shows both the icon and text side-by-side in the toolbar button face.
	/// By default, only the icon is shown and text appears as the label underneath.
	/// </summary>
	public static readonly BindableProperty ShowsTitleProperty =
		BindableProperty.Create(nameof(ShowsTitle), typeof(bool), typeof(MacOSMenuToolbarItem), false);
	public bool ShowsTitle { get => (bool)GetValue(ShowsTitleProperty); set => SetValue(ShowsTitleProperty, value); }

	/// <summary>The menu items to display in the dropdown.</summary>
	public IList<MacOSMenuItem> Items { get; } = new List<MacOSMenuItem>();

	public MacOSToolbarItemPlacement Placement { get; set; } = MacOSToolbarItemPlacement.Content;
}

// ── Toolbar Item Group ─────────────────────────────────────────────────

/// <summary>Selection behavior for a toolbar item group.</summary>
public enum MacOSToolbarGroupSelectionMode
{
	/// <summary>Radio-button style — exactly one segment selected at a time (like Finder view modes).</summary>
	SelectOne,
	/// <summary>Toggle style — any number of segments can be selected.</summary>
	SelectAny,
	/// <summary>Button-press style — segments light up momentarily, no persistent selection.</summary>
	Momentary,
}

/// <summary>How the group is visually represented in the toolbar.</summary>
public enum MacOSToolbarGroupRepresentation
{
	/// <summary>System decides based on available space.</summary>
	Automatic,
	/// <summary>Always show as individual separate buttons.</summary>
	Expanded,
	/// <summary>Always show as a compact segmented control.</summary>
	Collapsed,
}

/// <summary>A single segment in a <see cref="MacOSToolbarItemGroup"/>.</summary>
public class MacOSToolbarGroupSegment
{
	public string? Text { get; set; }
	/// <summary>SF Symbol icon name.</summary>
	public string? Icon { get; set; }
	/// <summary>Accessibility label for VoiceOver.</summary>
	public string? Label { get; set; }
	/// <summary>Whether this segment is selected (for <see cref="MacOSToolbarGroupSelectionMode.SelectAny"/>).</summary>
	public bool IsSelected { get; set; }
	public bool IsEnabled { get; set; } = true;
}

/// <summary>Event args for toolbar item group selection changes.</summary>
public class MacOSToolbarGroupSelectionChangedEventArgs : EventArgs
{
	public int SelectedIndex { get; }
	/// <summary>Array of selected states for each segment (useful for SelectAny mode).</summary>
	public bool[] SelectedSegments { get; }
	public MacOSToolbarGroupSelectionChangedEventArgs(int selectedIndex, bool[] selectedSegments)
	{
		SelectedIndex = selectedIndex;
		SelectedSegments = selectedSegments;
	}
}

/// <summary>
/// A group of toolbar items rendered as a segmented control or individual buttons.
/// Wraps <c>NSToolbarItemGroup</c>.
/// </summary>
public class MacOSToolbarItemGroup : BindableObject
{
	public IList<MacOSToolbarGroupSegment> Segments { get; } = new List<MacOSToolbarGroupSegment>();

	public static readonly BindableProperty SelectionModeProperty =
		BindableProperty.Create(nameof(SelectionMode), typeof(MacOSToolbarGroupSelectionMode),
			typeof(MacOSToolbarItemGroup), MacOSToolbarGroupSelectionMode.SelectOne);
	public MacOSToolbarGroupSelectionMode SelectionMode
	{
		get => (MacOSToolbarGroupSelectionMode)GetValue(SelectionModeProperty);
		set => SetValue(SelectionModeProperty, value);
	}

	public static readonly BindableProperty RepresentationProperty =
		BindableProperty.Create(nameof(Representation), typeof(MacOSToolbarGroupRepresentation),
			typeof(MacOSToolbarItemGroup), MacOSToolbarGroupRepresentation.Automatic);
	public MacOSToolbarGroupRepresentation Representation
	{
		get => (MacOSToolbarGroupRepresentation)GetValue(RepresentationProperty);
		set => SetValue(RepresentationProperty, value);
	}

	public static readonly BindableProperty SelectedIndexProperty =
		BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(MacOSToolbarItemGroup), 0);
	public int SelectedIndex
	{
		get => (int)GetValue(SelectedIndexProperty);
		set => SetValue(SelectedIndexProperty, value);
	}

	public static readonly BindableProperty LabelProperty =
		BindableProperty.Create(nameof(Label), typeof(string), typeof(MacOSToolbarItemGroup), string.Empty);
	public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

	public MacOSToolbarItemPlacement Placement { get; set; } = MacOSToolbarItemPlacement.Content;

	public event EventHandler<MacOSToolbarGroupSelectionChangedEventArgs>? SelectionChanged;
	internal void RaiseSelectionChanged(int selectedIndex, bool[] selectedSegments)
		=> SelectionChanged?.Invoke(this, new MacOSToolbarGroupSelectionChangedEventArgs(selectedIndex, selectedSegments));
}

// ── Share Toolbar Item ─────────────────────────────────────────────────

/// <summary>
/// The standard macOS share button (📤) that opens the system sharing sheet.
/// Wraps <c>NSSharingServicePickerToolbarItem</c>.
/// </summary>
public class MacOSShareToolbarItem : BindableObject
{
	/// <summary>
	/// Called when the share button is clicked to get the items to share.
	/// Return strings, URIs, or other shareable objects.
	/// </summary>
	public Func<IEnumerable<object>>? ShareItemsProvider { get; set; }

	public static readonly BindableProperty LabelProperty =
		BindableProperty.Create(nameof(Label), typeof(string), typeof(MacOSShareToolbarItem), "Share");
	public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

	public MacOSToolbarItemPlacement Placement { get; set; } = MacOSToolbarItemPlacement.Content;

	/// <summary>Fired when a sharing service is chosen by the user.</summary>
	public event EventHandler<string>? ServiceChosen;
	internal void RaiseServiceChosen(string serviceName) => ServiceChosen?.Invoke(this, serviceName);
}

// ── PopUp Toolbar Item ─────────────────────────────────────────────────

/// <summary>
/// A dropdown popup selector in the toolbar (like a font size or zoom level picker).
/// Wraps an <c>NSToolbarItem</c> with an <c>NSPopUpButton</c> as its view.
/// </summary>
public class MacOSPopUpToolbarItem : BindableObject
{
	/// <summary>The selectable options.</summary>
	public IList<string> Items { get; } = new List<string>();

	public static readonly BindableProperty SelectedIndexProperty =
		BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(MacOSPopUpToolbarItem), 0);
	public int SelectedIndex
	{
		get => (int)GetValue(SelectedIndexProperty);
		set => SetValue(SelectedIndexProperty, value);
	}

	/// <summary>Whether this is a pull-down menu (true) or popup selection (false, default).</summary>
	public static readonly BindableProperty PullsDownProperty =
		BindableProperty.Create(nameof(PullsDown), typeof(bool), typeof(MacOSPopUpToolbarItem), false);
	public bool PullsDown { get => (bool)GetValue(PullsDownProperty); set => SetValue(PullsDownProperty, value); }

	/// <summary>Width of the popup button (default: 120).</summary>
	public static readonly BindableProperty WidthProperty =
		BindableProperty.Create(nameof(Width), typeof(double), typeof(MacOSPopUpToolbarItem), 120.0);
	public double Width { get => (double)GetValue(WidthProperty); set => SetValue(WidthProperty, value); }

	public MacOSToolbarItemPlacement Placement { get; set; } = MacOSToolbarItemPlacement.Content;

	public event EventHandler<int>? SelectionChanged;
	internal void RaiseSelectionChanged(int index) => SelectionChanged?.Invoke(this, index);
}

// ── Custom View Toolbar Item ──────────────────────────────────────────

/// <summary>
/// A toolbar item that hosts an arbitrary MAUI <see cref="View"/> as its content.
/// The view is constrained to the toolbar height and an optional min/max width.
/// </summary>
public class MacOSViewToolbarItem : BindableObject
{
	/// <summary>The MAUI view to display in the toolbar.</summary>
	public static readonly BindableProperty ViewProperty =
		BindableProperty.Create(nameof(View), typeof(View), typeof(MacOSViewToolbarItem), null);
	public View? View { get => (View?)GetValue(ViewProperty); set => SetValue(ViewProperty, value); }

	/// <summary>Label shown in the customization palette and overflow menu.</summary>
	public string? Label { get; set; }

	/// <summary>
	/// Minimum width for the toolbar item. 0 means auto-size to content.
	/// </summary>
	public static readonly BindableProperty MinWidthProperty =
		BindableProperty.Create(nameof(MinWidth), typeof(double), typeof(MacOSViewToolbarItem), 0.0);
	public double MinWidth { get => (double)GetValue(MinWidthProperty); set => SetValue(MinWidthProperty, value); }

	/// <summary>
	/// Maximum width for the toolbar item. 0 means no maximum.
	/// </summary>
	public static readonly BindableProperty MaxWidthProperty =
		BindableProperty.Create(nameof(MaxWidth), typeof(double), typeof(MacOSViewToolbarItem), 0.0);
	public double MaxWidth { get => (double)GetValue(MaxWidthProperty); set => SetValue(MaxWidthProperty, value); }

	/// <summary>
	/// When true, wraps the view in a borderless NSButton that provides native
	/// toolbar hover highlighting and click states. The Clicked event fires on click.
	/// When false, the view is placed directly — handle interactions via MAUI gestures.
	/// </summary>
	public bool ShowsToolbarButtonStyle { get; set; }

	public MacOSToolbarItemPlacement Placement { get; set; } = MacOSToolbarItemPlacement.Content;

	/// <summary>Fired when the toolbar item is clicked (requires ShowsToolbarButtonStyle = true).</summary>
	public event EventHandler? Clicked;
	internal void RaiseClicked() => Clicked?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Attached properties for configuring the macOS toolbar layout at the page level.
/// </summary>
public static class MacOSToolbar
{
	/// <summary>
	/// When set on a <see cref="Page"/>, defines the exact layout of the sidebar toolbar area.
	/// Overrides the per-item <see cref="MacOSToolbarItem.PlacementProperty"/> convenience API.
	/// Items referenced here must also be in <see cref="Page.ToolbarItems"/>.
	/// Items NOT in this layout go to the content toolbar area (or <see cref="ContentLayoutProperty"/>).
	/// </summary>
	public static readonly BindableProperty SidebarLayoutProperty =
		BindableProperty.CreateAttached(
			"SidebarLayout",
			typeof(IList<MacOSToolbarLayoutItem>),
			typeof(MacOSToolbar),
			defaultValue: null,
			propertyChanged: OnToolbarAttachedPropertyChanged);

	public static IList<MacOSToolbarLayoutItem>? GetSidebarLayout(BindableObject obj)
		=> (IList<MacOSToolbarLayoutItem>?)obj.GetValue(SidebarLayoutProperty);

	public static void SetSidebarLayout(BindableObject obj, IList<MacOSToolbarLayoutItem>? value)
		=> obj.SetValue(SidebarLayoutProperty, value ?? new List<MacOSToolbarLayoutItem>());

	/// <summary>
	/// When set on a <see cref="Page"/>, defines the exact layout of the content toolbar area
	/// (right of the tracking separator). Items referenced here must also be in
	/// <see cref="Page.ToolbarItems"/>. When not set, content items are laid out with
	/// the default [flex] [title] [flex] [items...] pattern.
	/// </summary>
	public static readonly BindableProperty ContentLayoutProperty =
		BindableProperty.CreateAttached(
			"ContentLayout",
			typeof(IList<MacOSToolbarLayoutItem>),
			typeof(MacOSToolbar),
			defaultValue: null,
			propertyChanged: OnToolbarAttachedPropertyChanged);

	public static IList<MacOSToolbarLayoutItem>? GetContentLayout(BindableObject obj)
		=> (IList<MacOSToolbarLayoutItem>?)obj.GetValue(ContentLayoutProperty);

	public static void SetContentLayout(BindableObject obj, IList<MacOSToolbarLayoutItem>? value)
		=> obj.SetValue(ContentLayoutProperty, value ?? new List<MacOSToolbarLayoutItem>());

	/// <summary>
	/// When set on a <see cref="Page"/>, adds a native <see cref="NSSearchToolbarItem"/>
	/// to the toolbar. Use <see cref="MacOSSearchToolbarItem.Placement"/> to control
	/// where it appears, or include it in an explicit layout with
	/// <see cref="MacOSToolbarLayoutItem.Search"/>.
	/// </summary>
	public static readonly BindableProperty SearchItemProperty =
		BindableProperty.CreateAttached(
			"SearchItem",
			typeof(MacOSSearchToolbarItem),
			typeof(MacOSToolbar),
			defaultValue: null,
			propertyChanged: OnToolbarAttachedPropertyChanged);

	public static MacOSSearchToolbarItem? GetSearchItem(BindableObject obj)
		=> (MacOSSearchToolbarItem?)obj.GetValue(SearchItemProperty);

	public static void SetSearchItem(BindableObject obj, MacOSSearchToolbarItem? value)
		=> obj.SetValue(SearchItemProperty, value);

	/// <summary>Menu toolbar items to add to the toolbar.</summary>
	public static readonly BindableProperty MenuItemsProperty =
		BindableProperty.CreateAttached("MenuItems", typeof(IList<MacOSMenuToolbarItem>),
			typeof(MacOSToolbar), defaultValue: null, propertyChanged: OnToolbarAttachedPropertyChanged);

	public static IList<MacOSMenuToolbarItem>? GetMenuItems(BindableObject obj)
		=> (IList<MacOSMenuToolbarItem>?)obj.GetValue(MenuItemsProperty);
	public static void SetMenuItems(BindableObject obj, IList<MacOSMenuToolbarItem>? value)
		=> obj.SetValue(MenuItemsProperty, value);

	/// <summary>Toolbar item groups (segmented controls) to add.</summary>
	public static readonly BindableProperty ItemGroupsProperty =
		BindableProperty.CreateAttached("ItemGroups", typeof(IList<MacOSToolbarItemGroup>),
			typeof(MacOSToolbar), defaultValue: null, propertyChanged: OnToolbarAttachedPropertyChanged);

	public static IList<MacOSToolbarItemGroup>? GetItemGroups(BindableObject obj)
		=> (IList<MacOSToolbarItemGroup>?)obj.GetValue(ItemGroupsProperty);
	public static void SetItemGroups(BindableObject obj, IList<MacOSToolbarItemGroup>? value)
		=> obj.SetValue(ItemGroupsProperty, value);

	/// <summary>Share toolbar item to add.</summary>
	public static readonly BindableProperty ShareItemProperty =
		BindableProperty.CreateAttached("ShareItem", typeof(MacOSShareToolbarItem),
			typeof(MacOSToolbar), defaultValue: null, propertyChanged: OnToolbarAttachedPropertyChanged);

	public static MacOSShareToolbarItem? GetShareItem(BindableObject obj)
		=> (MacOSShareToolbarItem?)obj.GetValue(ShareItemProperty);
	public static void SetShareItem(BindableObject obj, MacOSShareToolbarItem? value)
		=> obj.SetValue(ShareItemProperty, value);

	/// <summary>PopUp toolbar items (dropdown selectors) to add.</summary>
	public static readonly BindableProperty PopUpItemsProperty =
		BindableProperty.CreateAttached("PopUpItems", typeof(IList<MacOSPopUpToolbarItem>),
			typeof(MacOSToolbar), defaultValue: null, propertyChanged: OnToolbarAttachedPropertyChanged);

	public static IList<MacOSPopUpToolbarItem>? GetPopUpItems(BindableObject obj)
		=> (IList<MacOSPopUpToolbarItem>?)obj.GetValue(PopUpItemsProperty);
	public static void SetPopUpItems(BindableObject obj, IList<MacOSPopUpToolbarItem>? value)
		=> obj.SetValue(PopUpItemsProperty, value);

	/// <summary>Custom view toolbar items to add.</summary>
	public static readonly BindableProperty ViewItemsProperty =
		BindableProperty.CreateAttached("ViewItems", typeof(IList<MacOSViewToolbarItem>),
			typeof(MacOSToolbar), defaultValue: null, propertyChanged: OnToolbarAttachedPropertyChanged);

	public static IList<MacOSViewToolbarItem>? GetViewItems(BindableObject obj)
		=> (IList<MacOSViewToolbarItem>?)obj.GetValue(ViewItemsProperty);
	public static void SetViewItems(BindableObject obj, IList<MacOSViewToolbarItem>? value)
		=> obj.SetValue(ViewItemsProperty, value);

	/// <summary>
	/// Forces the ToolbarHandler to refresh by triggering a ToolbarItems
	/// collection change, since attached property changes on Page aren't
	/// reliably detected by the handler's PropertyChanged subscription.
	/// </summary>
	static readonly ToolbarItem _sentinel = new() { AutomationId = "__MacOSToolbar_Sentinel__" };

	static void OnToolbarAttachedPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		if (bindable is Page page && page.ToolbarItems != null)
		{
			// Add/remove a sentinel item to trigger CollectionChanged → RefreshToolbar.
			// Remove it asynchronously so only ONE synchronous refresh fires with the
			// new property value already set. The async removal fires a second refresh
			// which also succeeds because the attached property is already set.
			if (!page.ToolbarItems.Contains(_sentinel))
			{
				page.ToolbarItems.Add(_sentinel);
				page.Dispatcher?.Dispatch(() => page.ToolbarItems.Remove(_sentinel));
			}
		}
	}

	/// <summary>
	/// Triggers a toolbar refresh for the given page. Used by
	/// <see cref="MacOSToolbarItem.IsVisibleProperty"/> when a ToolbarItem's
	/// visibility changes at runtime.
	/// </summary>
	internal static void RequestRefresh(Page page)
		=> OnToolbarAttachedPropertyChanged(page, null, null);
}
