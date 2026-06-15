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
		// SwiftUI alert deferred (Android-first) — empty own-content placeholder so the shared
		// tree still materializes on iOS without rendering the dialog's slots inline.
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUIAlertDialogNode();
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
}
#endif
