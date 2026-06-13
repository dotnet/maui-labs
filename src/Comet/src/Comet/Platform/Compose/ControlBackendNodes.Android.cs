#nullable enable
#if ANDROID
using Comet.Backend;
using Comet.Platform.Compose;

namespace Comet
{
	// CreateBackendNode overrides wiring each wave-1 control to its Compose node.
	// These are the ONLY references to the concrete node types, so a control the app
	// never uses trims away together with its node.

	public partial class Text
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeTextNode();
	}

	public partial class Button
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeButtonNode();
	}

	public partial class VStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeStackNode(StackAxis.Vertical);
	}

	public partial class HStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeStackNode(StackAxis.Horizontal);
	}

	public partial class ZStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeStackNode(StackAxis.Depth);
	}

	public partial class TextField
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeTextFieldNode();
	}

	public partial class Toggle
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeToggleNode();
	}

	public partial class Slider
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeSliderNode();
	}

	public partial class ListView
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeListNode(this, context);
	}

	public partial class NavigationView
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new ComposeNavigationNode(this, context);
	}
}
#endif
