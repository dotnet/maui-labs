#nullable enable
#if IOS
using Comet.Backend;
using Comet.Platform.SwiftUI;

namespace Comet
{
	// CreateBackendNode overrides mapping wave-1 controls to their SwiftUI node kind.
	// The only references to SwiftUINode, so unused controls trim with their nodes.

	public partial class Text
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("text");
	}

	public partial class FormattedText
	{
		// iOS renders the concatenated text (per-run styling is a follow-up); the node folds runs
		// into the plain "text" value.
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("text");
	}

	public partial class Button
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("button");
	}

	public partial class AlertDialog
	{
		// Native SwiftUI .alert (the dialog's Text/ConfirmButton flatten to message + button label).
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIAlertDialogNode(this);
	}

	public partial class SelectorPanel
	{
		// Android-first: the input-selector panel never expands on iOS (no-op twin).
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUISelectorPanelNode(this, context);
	}

	public partial class Fab
	{
		// Native SwiftUI composition (iOS has no Material FAB); pending simulator verification.
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIFabNode(this, context);
	}

	public partial class VStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("vstack");
	}

	public partial class HStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("hstack");
	}

	public partial class ZStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("zstack");
	}

	public partial class TextField
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("textfield");
	}

	public partial class Toggle
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("toggle");
	}

	public partial class Image
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("image");
	}

	public partial class Icon
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("icon");
	}

	public partial class Drawer
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIDrawerNode(this, context);
	}

	public partial class Slider
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("slider");
	}

	public partial class ListView
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIListNode(this, context);
	}

	public partial class ScrollView
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIScrollNode(this, context);
	}

	public partial class NavigationView
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINavigationNode(this, context);
	}

	// Adaptive primitives (M1 Reply): hosted-composition twins — structure/interaction
	// parity composed from Comet views (see SwiftUIAdaptiveNodes.cs).

	public partial class ContentSwitcher
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIContentSwitcherNode(this, context);
	}

	public partial class TabBar
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUITabRowNode(this, context);
	}

	public partial class IconToggleButton
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIIconToggleNode(this, context);
	}

	public partial class FilterChip
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIFilterChipNode(this, context);
	}

	public partial class ListDetail
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIListDetailNode(this, context);
	}

	public partial class NavigationSuite
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINavigationSuiteNode(this, context);
	}

	public partial class SearchBar
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUISearchBarNode(this, context);
	}

	public partial class NavigationBar
	{
		// Standalone bar: host a one-variant suite-style row (rare outside the suite).
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINavigationSuiteNode(
				new NavigationSuite(SelectedIndex, Items, new VStack()), context);
	}

	public partial class NavigationRail
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINavigationSuiteNode(
				new NavigationSuite(SelectedIndex, Items, new VStack(), railHeader: HeaderView), context);
	}
}
#endif
