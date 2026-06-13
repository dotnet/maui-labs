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

	public partial class Button
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("button");
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
}
#endif
